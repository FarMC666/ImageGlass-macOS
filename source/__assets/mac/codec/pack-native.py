#!/usr/bin/env python3
"""Package a verified Intel dylib using stable ZIP metadata and entry order."""

import argparse
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile, ZipInfo

PACKAGE_ID = 'FarMC666.ImageGlass.Magick.Native.osx-x64'
PACKAGE_VERSION = '15.1.0-intel.1'
LIBRARY = 'Magick.Native-Q16-HDRI-x64.dll.dylib'

NUSPEC = f"""<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata>
    <id>{PACKAGE_ID}</id>
    <version>{PACKAGE_VERSION}</version>
    <authors>FarMC666; Dirk Lemstra; ImageMagick contributors</authors>
    <description>Intel macOS Q16-HDRI Magick.Native with ImageMagick 7.1.2-36 for Magick.NET 15.1.0.</description>
    <license type="file">NOTICE.txt</license>
    <projectUrl>https://github.com/FarMC666/ImageGlass-macOS</projectUrl>
  </metadata>
</package>
"""

CONTENT_TYPES = """<?xml version="1.0" encoding="utf-8"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="nuspec" ContentType="application/octet"/>
  <Default Extension="txt" ContentType="text/plain"/>
  <Default Extension="tsv" ContentType="text/tab-separated-values"/>
  <Default Extension="dylib" ContentType="application/octet-stream"/>
</Types>
"""


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('library', type=Path)
    parser.add_argument('notice', type=Path)
    parser.add_argument('feed', type=Path)
    args = parser.parse_args()
    args.feed.mkdir(parents=True, exist_ok=True)
    output = args.feed / f'{PACKAGE_ID}.{PACKAGE_VERSION}.nupkg'
    entries = {
        '[Content_Types].xml': CONTENT_TYPES,
        f'{PACKAGE_ID}.nuspec': NUSPEC,
        'NOTICE.txt': args.notice.read_bytes(),
        'sources.tsv': Path(__file__).with_name('sources.tsv').read_bytes(),
        f'runtimes/osx-x64/native/{LIBRARY}': args.library.read_bytes(),
    }
    with ZipFile(output, 'w') as package:
        for name, data in sorted(entries.items()):
            info = ZipInfo(name)
            info.create_system = 3
            info.external_attr = (0o100755 if name.endswith('.dylib') else 0o100644) << 16
            package.writestr(info, data, compress_type=ZIP_DEFLATED, compresslevel=9)
    print(output)


if __name__ == '__main__':
    main()
