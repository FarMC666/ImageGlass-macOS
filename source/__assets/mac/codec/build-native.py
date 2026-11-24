#!/usr/bin/env python3
"""Build the pinned Intel Magick.Native library with isolated static dependencies."""

import argparse
import hashlib
import os
from pathlib import Path
import platform
import re
import shutil
import subprocess
import tarfile


def run(*command, cwd, env=None):
    subprocess.run(command, cwd=cwd, env=env, check=True)


def digest(path):
    result = hashlib.sha256()
    with path.open('rb') as source:
        for chunk in iter(lambda: source.read(1024 * 1024), b''):
            result.update(chunk)
    return result.hexdigest()


def extract(archive, destination):
    if destination.exists():
        shutil.rmtree(destination)
    destination.mkdir(parents=True)
    run('tar', '-xzf', archive, '--strip-components=1', cwd=destination)


def replace(path, before, after):
    original = path.read_text()
    if before not in original:
        raise RuntimeError(f'Pinned script changed: {before!r} in {path}')
    path.write_text(original.replace(before, after))


def prepare_dependencies(deps, prefix, build, jobs):
    scripts = deps / '.github/build'
    entry = scripts / 'macos-x64/build.sh'
    entry.parent.mkdir()
    shutil.copyfile(scripts / 'macos-arm64/build.sh', entry)
    replace(entry, '/tmp/dependencies', str(prefix))
    replace(entry, 'make -j$(sysctl -n hw.logicalcpu)', f'make -j{jobs}')
    replace(entry, '-O3 -fPIC -DNDEBUG', f'-O3 -fPIC -DNDEBUG -ffile-prefix-map={build}=/build')
    replace(entry, 'export CMAKE_OPTIONS=""', 'export CMAKE_OPTIONS="-DCMAKE_POLICY_VERSION_MINIMUM=3.5"')
    replace(entry, 'export PATH=/usr/local/opt/gnu-sed/libexec/gnubin:$PATH:/Library/Frameworks/Python.framework/Versions/3.11/bin',
            'export PATH="$INSTALL_PREFIX/bin:$PATH"')
    # Shell helpers must remain inside the build prefix.
    glib = scripts / 'shared/glib.sh'
    replace(glib, 'cp $INSTALL_PREFIX/bin/glib-genmarshal /usr/local/bin/glib-genmarshal', '')
    replace(glib, 'cp $INSTALL_PREFIX/bin/glib-mkenums /usr/local/bin/glib-mkenums', '')
    replace(glib, 'cd Dependencies/glib', """cd Dependencies/glib
sed -i "s|format(glib_giomodulesdir)|format('/usr/lib/gio/modules')|" gio/meson.build
sed -i "s|format(glib_libdir)|format('/usr/lib')|" glib/gnulib/meson.build
sed -i "s|format(glib_charsetaliasdir)|format('/usr/lib')|" glib/libcharset/meson.build""")
    lqr = scripts / 'shared/lqr.sh'
    replace(lqr, 'mkdir $INSTALL_PREFIX/include/lqr-1', 'mkdir -p $INSTALL_PREFIX/include/lqr-1')
    # The application links the FriBidi library and does not ship its CLI or manuals.
    fribidi = scripts / 'shared/fribidi.sh'
    replace(fribidi, 'cd Dependencies/fribidi', """cd Dependencies/fribidi
sed -i 's/^SUBDIRS = gen.tab lib bin doc test$/SUBDIRS = gen.tab lib/' Makefile.am""")
    # Keep private build paths out of fontconfig's compiled defaults. The
    # generated install files still go to the isolated static prefix.
    fontconfig = scripts / 'shared/fontconfig.sh'
    replace(fontconfig, 'cd NonWindowsDependencies/fontconfig',
            r"""cd NonWindowsDependencies/fontconfig
sed -i \
  -e "s|'CONFIGDIR', fc_configdir)|'CONFIGDIR', '/etc/fonts/conf.d')|" \
  -e "s|'FC_CACHEDIR', fc_cachedir)|'FC_CACHEDIR', '/var/cache/fontconfig')|" \
  -e "s|'FC_TEMPLATEDIR', fc_templatedir)|'FC_TEMPLATEDIR', '/usr/share/fontconfig/conf.avail')|" \
  -e "s|'FONTCONFIG_PATH', fc_baseconfigdir)|'FONTCONFIG_PATH', '/etc/fonts')|" \
  meson.build""")
    rsvg = scripts / 'shared/rsvg.sh'
    replace(rsvg, 'cd Dependencies/rsvg', r"""cd Dependencies/rsvg
sed -i 's|SRCDIR "/tests/resources/|"/usr/share/imageglass-codec/test-fixtures/|' rsvg-cairo-draw.c""")
    # The tagged OpenEXR 3.5.5 source has no pre-generated build headers.
    exr = scripts / 'shared/exr.sh'
    exr.write_text(re.sub(r'^rm .*\.h\n', '', exr.read_text(), flags=re.MULTILINE))
    replace(exr, '-DBUILD_TESTING=false', '-DBUILD_TESTING=false -DOPENEXR_USE_INTERNAL_OPENJPH=OFF')
    # Upstream's top-level entry supplies common flags and dependency order. Persist
    # completed steps so a failed tool invocation can be retried in isolation.
    body = entry.read_text()
    start = body.index('# Build dependencies')
    body = body[:start] + """run_dependency() {
    script="$1"
    name="$(basename "$script" .sh)"
    marker="$CODEC_BUILD_STATE/$name"
    [ ! -f "$marker" ] || return 0
    printf '\n=== Building %s ===\n' "$name"
    "$script"
    touch "$marker"
}
""" + body[start:]
    entry.write_text(re.sub(r'^([\$]SHARED_PATH/.*\.sh)$', r'run_dependency "\1"', body, flags=re.MULTILINE))
    return entry


def prepare_native(native, prefix, magick_prefix, jobs):
    build = native / 'build'
    settings = build / 'macos-x64/settings.sh'
    settings.parent.mkdir()
    shutil.copyfile(build / 'macos-arm64/settings.sh', settings)
    replace(settings, '/tmp/dependencies', str(prefix))
    replace(settings, 'make -j$(sysctl -n hw.logicalcpu)', f'make -j{jobs}')
    replace(settings, '-O3 -fPIC -Wall -DNDEBUG', f'-O3 -fPIC -Wall -DNDEBUG -ffile-prefix-map={native.parent}=/build')
    replace(settings, 'export CMAKE_OPTIONS=""',
            'export CMAKE_OPTIONS="-DCMAKE_OSX_ARCHITECTURES=x86_64 -DCMAKE_OSX_DEPLOYMENT_TARGET=15.0 -DCMAKE_POLICY_VERSION_MINIMUM=3.5"')
    imagemagick = build / 'shared/build.imagemagick.sh'
    replace(imagemagick, '/tmp/ImageMagick', str(magick_prefix))
    replace(imagemagick, '--enable-static --enable-delegate-build',
            '--enable-static --disable-installed --enable-delegate-build')
    # The configured paths describe the installed layout, independent of the build prefix.
    replace(imagemagick, '  $MAKE install',
            f"""  sed -i 's|{magick_prefix}|/usr/share/imageglass-codec|g' MagickCore/magick-baseconfig.h MagickCore/version.h
  $MAKE install""")
    cmake = native / 'src/Magick.Native/CMakeLists.txt'
    replace(cmake, '/tmp/ImageMagick', str(magick_prefix))
    replace(cmake, '/tmp/dependencies', str(prefix))



def write_notices(build, downloads, manifests):
    documents = [
        'Magick.Native and ImageMagick static dependencies for ImageGlass Intel.\n',
        'Pinned corresponding sources and SHA-256 digests are in sources.tsv.\n',
    ]
    license_name = re.compile(r'^(license|licence|copying|copyright|notice|patents)([._-]|$)', re.IGNORECASE)
    for line in manifests:
        if not line or line.startswith('#'):
            continue
        name, repository, revision, _ = line.split()
        if name == 'Dependencies':
            continue
        documents.append(f'\n{name}\nhttps://github.com/{repository}/tree/{revision}\n')
        with tarfile.open(downloads / f'{name}.tar.gz', 'r:gz') as archive:
            texts = set()
            for member in sorted(archive, key=lambda entry: entry.name):
                parts = member.name.split('/')[1:]
                primary = len(parts) == 1 and license_name.match(parts[-1])
                freetype_terms = name == 'freetype' and parts in (['docs', 'FTL.TXT'], ['docs', 'GPLv2.TXT'])
                if not (primary or freetype_terms) or not (member.isfile() or member.issym()):
                    continue
                text = archive.extractfile(member).read().decode('utf-8', errors='replace')
                if text not in texts:
                    texts.add(text)
                    documents.append('\n' + '/'.join(parts) + '\n' + text + '\n')
            if not texts:
                raise RuntimeError(f'No license information found for {name}')
    (build / 'NOTICE.txt').write_text(''.join(documents), encoding='utf-8')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('build', type=Path, help='Dedicated build directory')
    parser.add_argument('--downloads', required=True, type=Path, help='Cache of pinned source archives')
    parser.add_argument('--jobs', type=int, default=8)
    parser.add_argument('--resume', action='store_true', help='Reuse completed dependency builds after a failure')
    args = parser.parse_args()
    if platform.system() != 'Darwin' or platform.machine() != 'x86_64':
        parser.error('Run this build on Intel macOS.')
    if args.jobs < 1:
        parser.error('--jobs must be positive')
    build = args.build.resolve()
    if ' ' in str(build):
        parser.error('The upstream scripts require a build path without spaces.')
    build.mkdir(parents=True, exist_ok=True)
    downloads = args.downloads.resolve()
    downloads.mkdir(parents=True, exist_ok=True)
    prefix = build / 'static'
    magick_prefix = build / 'imagemagick'
    state = build / 'state'
    if args.resume and not state.is_dir():
        parser.error('--resume requires an existing codec build directory.')
    if not args.resume and any(build.iterdir()):
        parser.error('Select an empty build directory or pass --resume for a previous build.')
    state.mkdir(exist_ok=True)
    deps = build / 'Dependencies'
    native = build / 'Magick.Native'
    magick = native / 'src/ImageMagick/ImageMagick'
    manifests = Path(__file__).with_name('sources.tsv').read_text().splitlines()
    archives = {}
    for line in manifests:
        if not line or line.startswith('#'):
            continue
        name, repository, revision, expected = line.split()
        archive = downloads / f'{name}.tar.gz'
        if not archive.exists():
            temporary = archive.with_suffix('.download')
            run('curl', '-fLsS', '--retry', '2', f'https://codeload.github.com/{repository}/tar.gz/{revision}',
                '-o', temporary, cwd=downloads)
            if digest(temporary) != expected:
                raise RuntimeError(f'Unexpected SHA-256 of {name}')
            temporary.rename(archive)
        if digest(archive) != expected:
            raise RuntimeError(f'Unexpected SHA-256 of {archive}')
        archives[name] = archive

    env = {key: value for key, value in os.environ.items() if key not in {
        'CPATH', 'C_INCLUDE_PATH', 'CPLUS_INCLUDE_PATH', 'LIBRARY_PATH', 'DYLD_LIBRARY_PATH',
        'CMAKE_PREFIX_PATH', 'CFLAGS', 'CXXFLAGS', 'CPPFLAGS', 'LDFLAGS', 'PKG_CONFIG_PATH',
        'DEBUG_BUILD', 'SDKROOT', 'MACOSX_DEPLOYMENT_TARGET',
    }}
    extra_bin = build / 'tools'
    extra_bin.mkdir(exist_ok=True)
    for name, tool in [('sed', 'gsed'), ('libtoolize', 'glibtoolize')]:
        target = extra_bin / name
        binary = shutil.which(tool)
        if binary is None:
            raise RuntimeError(f'Missing required tool {tool}')
        if not target.exists():
            target.symlink_to(binary)
    env.update(
        PATH=f'{extra_bin}:{env["PATH"]}',
        MACOSX_DEPLOYMENT_TARGET='15.0',
        SOURCE_DATE_EPOCH='1794856023',
        ZERO_AR_DATE='1',
        PKG_CONFIG_LIBDIR=f'{prefix}/lib/pkgconfig:{prefix}/share/pkgconfig',
        LC_ALL='C',
        lt_cv_sys_max_cmd_len='131072',
        TZ='UTC',
        CC='/usr/bin/clang',
        CXX='/usr/bin/clang++',
        CMAKE_POLICY_VERSION_MINIMUM='3.5',
        SDKROOT=subprocess.check_output(['xcrun', '--sdk', 'macosx', '--show-sdk-path'], text=True).strip(),
        CODEC_BUILD_STATE=str(state),
        CMAKE_PREFIX_PATH=str(prefix),
    )

    extract(archives.pop('Dependencies'), deps)
    extract(archives.pop('Magick.Native'), native)
    extract(archives.pop('ImageMagick'), magick)
    for name, archive in archives.items():
        parent = 'NonWindowsDependencies' if name == 'fontconfig' else 'Dependencies'
        extract(archive, deps / parent / name)

    entry = prepare_dependencies(deps, prefix, build, args.jobs)
    prepare_native(native, prefix, magick_prefix, args.jobs)
    run('bash', entry, cwd=deps, env=env)
    run('bash', '../../../build/shared/build.imagemagick.sh', 'macos', 'x64', 'Q16-HDRI', cwd=magick, env=env)
    run('bash', '../../build/shared/build.native.sh', 'macos', 'x64', 'Q16-HDRI', cwd=native / 'src/Magick.Native', env=env)
    run('bash', '../../build/shared/copy.native.sh', '../../artifacts', 'macos', 'x64', 'Q16-HDRI',
        cwd=native / 'src/Magick.Native', env=env)
    write_notices(build, downloads, manifests)


if __name__ == '__main__':
    main()
