import struct, sys, zlib, os

def load(path):
    data = open(path, 'rb').read()
    if data[:4] == b'Z2HM':
        return zlib.decompress(data[8:])
    return data

raw = load(sys.argv[1])
for spec in sys.argv[2:]:
    a, b = spec.split('-')
    a, b = int(a, 0), int(b, 0)
    print(f'=== 0x{a:X}..0x{b:X}')
    for i in range(a, b, 16):
        ch = raw[i:i+16]
        hx = ' '.join(f'{x:02X}' for x in ch)
        asc = ''.join(chr(x) if 32 <= x < 127 else '.' for x in ch)
        print(f'{i:04X}  {hx:<47}  {asc}')
