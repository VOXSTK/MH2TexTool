import struct, sys, zlib, os

def load(path):
    data = open(path, 'rb').read()
    if data[:4] == b'Z2HM':
        return zlib.decompress(data[8:])
    return data

def u32(raw, o):
    return struct.unpack_from('<I', raw, o)[0]

def cstr(raw, o, maxlen=96):
    e = raw.find(b'\0', o)
    if e < 0 or e - o > maxlen:
        e = o + maxlen
    return raw[o:e].decode('latin1', 'replace')

def desc(raw, off):
    w, h, a, b, c = struct.unpack_from('<5I', raw, off)
    mips = struct.unpack_from('<H', raw, off+20)[0]
    doff = u32(raw, off+24)
    fmt = raw[off+28]
    size = u32(raw, off+32)
    return dict(w=w, h=h, a=a, b=b, c=c, mips=mips, doff=doff, fmt=fmt, size=size)

def dds(raw, off):
    if off+128 > len(raw) or raw[off:off+4] != b'DDS ':
        return None
    _, size, flags, h, w, pitch, depth, mips = struct.unpack_from('<8I', raw, off+4)
    fourcc = raw[off+0x54:off+0x58]
    return dict(w=w, h=h, mips=mips, fourcc=fourcc.decode('latin1'), pitch=pitch, size=size)

path = sys.argv[1]
raw = load(path)
count = u32(raw, 0x20)
print(f'{os.path.basename(path)} raw={len(raw)} count={count} u24={u32(raw,0x24):#x} u28={u32(raw,0x28):#x}')

o = 0x40
seen = set()
for i in range(count + 5):
    if o in seen or o + 0x40 > len(raw):
        print(f'  [stop at rec {i} offset {o:#x}]')
        break
    seen.add(o)
    nxt = u32(raw, o)
    prv = u32(raw, o+4)
    name = cstr(raw, o+8)
    d = desc(raw, nxt - 0x38) if nxt - 0x38 > o else None
    dd = dds(raw, d['doff']) if d else None
    print(f'{i:3d} rec@{o:#07x} next={nxt:#07x} prev={prv:#07x} name={name!r}')
    if d:
        print(f'      desc@{nxt-0x38:#07x} w={d["w"]} h={d["h"]} a={d["a"]} b={d["b"]} c={d["c"]} mips={d["mips"]} off={d["doff"]:#x} fmt={d["fmt"]} size={d["size"]} ({d["size"]})')
    if dd:
        print(f'      dds: {dd["w"]}x{dd["h"]} mips={dd["mips"]} fourcc={dd["fourcc"]} pitch={dd["pitch"]} hdr={dd["size"]}')
    else:
        print('      dds: <none / not DDS>')
    o = nxt
