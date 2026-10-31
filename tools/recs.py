import struct, sys, zlib, os, re

def load(path):
    data = open(path, 'rb').read()
    if data[:4] == b'Z2HM':
        return zlib.decompress(data[8:])
    return data

raw = load(sys.argv[1])
n = int(sys.argv[2]) if len(sys.argv) > 2 else 8
print(f'{os.path.basename(sys.argv[1])} raw={len(raw)}')

dd = []
i = 0
while True:
    i = raw.find(b'DDS ', i)
    if i < 0: break
    dd.append(i); i += 4
print('textures found by DDS scan:', len(dd))
print('first DDS offsets:', [hex(x) for x in dd[:6]], '... last:', hex(dd[-1]) if dd else None)

count = struct.unpack_from('<I', raw, 0x20)[0]
print('header count @0x20 =', count)
print('header table entries @0x24:', [hex(struct.unpack_from('<I', raw, 0x24+4*k)[0]) for k in range(min(count,6))])

for k in range(n):
    b = 0x40 + 0x80*k
    if b + 0x80 > len(raw): break
    f = lambda o, fmt='<I': struct.unpack_from(fmt, raw, b+o)[0]
    name = raw[b+8:b+8+32].split(b'\0')[0].decode('latin1')
    print(f'--- rec {k} @0x{b:X}  next?={f(0):#x}  f4={f(4):#x}  name={name!r}')
    print(f'    +0x48={f(0x48)} +0x4C={f(0x4C)} +0x50={f(0x50)} +0x54={f(0x54)} +0x58={f(0x58)} +0x5C(u16)={f(0x5C,"<H")} +0x60={f(0x60):#x} +0x64(u8)={f(0x64,"<B")} +0x68={f(0x68):#x} ({f(0x68)}) +0x6C={f(0x6C):#x} +0x70={f(0x70)} +0x74={f(0x74)} +0x78={f(0x78)} +0x7C={f(0x7C)}')
