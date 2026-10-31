import struct, sys, zlib, os, re

def load(path):
    data = open(path, 'rb').read()
    if data[:4] == b'Z2HM':
        return zlib.decompress(data[8:])
    return data

def ddss(raw):
    out = []
    i = 0
    while True:
        i = raw.find(b'DDS ', i)
        if i < 0:
            break
        out.append(i)
        i += 4
    return out

def info(raw, off):
    # off points at 'DDS '
    size, flags, h, w, pitch, depth, mips = struct.unpack_from('<7I', raw, off+4)
    pf_size, pf_flags = struct.unpack_from('<2I', raw, off+0x4C+0x00-0x00+0x4C*0+0x00)
    return dict(off=off, size=size, flags=hex(flags), w=w, h=h, pitch=pitch, depth=depth, mips=mips)

def dwords(raw, start, end):
    for a in range(start, end, 4):
        v = struct.unpack_from('<I', raw, a)[0]
        asc = raw[a:a+4]
        printable = ''.join(chr(c) if 32 <= c < 127 else '.' for c in asc)
        print(f'{a:04X}  {v:10d}  0x{v:08X}  {printable}')

def strings(raw, start, end, minlen=3):
    for m in re.finditer(rb'[\x20-\x7E]{%d,80}' % minlen, raw[start:end]):
        print(f'  0x{start+m.start():04X}  {m.group().decode()}')

path = sys.argv[1]
raw = load(path)
print(f'== {os.path.basename(path)} raw={len(raw)}')
hdr = struct.unpack_from('<4sIIIIIIIIII', raw, 0)
print('header dwords:')
for i in range(0, 0x40, 4):
    v = struct.unpack_from('<I', raw, i)[0]
    print(f'  +0x{i:02X} = {v:10d}  0x{v:08X}')
print('DDS offsets:', [hex(x) for x in ddss(raw)])
print('first 4 text-ish runs in 0x40..first DDS:')
d0 = ddss(raw)[0] if ddss(raw) else len(raw)
strings(raw, 0x40, d0)
print('dwords 0x40..0x40+0x40:')
dwords(raw, 0x40, 0x80)
print('dwords first record tail (last 0x60 bytes before first DDS):')
dwords(raw, d0-0x60, d0)
