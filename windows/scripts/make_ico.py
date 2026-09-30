"""Pack PNG images into a Windows .ico (PNG-compressed entries, supported since Vista).
Usage: make_ico.py out.ico in16.png in32.png ... (square PNGs, <= 256px)"""
import struct, sys

out, pngs = sys.argv[1], sys.argv[2:]
blobs = [open(p, 'rb').read() for p in pngs]
header = struct.pack('<HHH', 0, 1, len(blobs))
offset = 6 + 16 * len(blobs)
entries = b''
for data in blobs:
    w, h = struct.unpack('>II', data[16:24])  # PNG IHDR width/height
    entries += struct.pack('<BBBBHHII', w % 256, h % 256, 0, 0, 1, 32, len(data), offset)
    offset += len(data)
open(out, 'wb').write(header + entries + b''.join(blobs))
print(f'wrote {out} ({len(blobs)} sizes)')
