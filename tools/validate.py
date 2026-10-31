import struct, sys, zlib, os

def load(path):
    data = open(path, 'rb').read()
    if data[:4] == b'Z2HM':
        return zlib.decompress(data[8:])
    return data

def u32(raw, o): return struct.unpack_from('<I', raw, o)[0]
def u16(raw, o): return struct.unpack_from('<H', raw, o)[0]

def cstr(raw, o, maxlen=128):
    e = raw.find(b'\0', o)
    if e < 0 or e - o > maxlen: e = o + maxlen
    return raw[o:e].decode('latin1', 'replace')

def dds_info(raw, off):
    if raw[off:off+4] != b'DDS ': return None
    size, flags, h, w, pitch, depth, mips = struct.unpack_from('<7I', raw, off+4)
    fourcc = raw[off+0x54:off+0x58].decode('latin1')
    return dict(w=w, h=h, mips=mips, fourcc=fourcc, pitch=pitch, hdr=size, total=off)

def dds_offsets(raw):
    out, i = [], 0
    while True:
        i = raw.find(b'DDS ', i)
        if i < 0: break
        d = dds_info(raw, i)
        if d and d['hdr'] == 0x7C and 0 < d['w'] <= 4096 and 0 < d['h'] <= 4096:
            out.append(i)
        i += 4
    return out

def chain(raw):
    sentinel = 0x24
    o = u32(raw, sentinel)
    nodes, seen = [], set()
    while o != sentinel and o not in seen and 0x40 <= o < len(raw):
        seen.add(o)
        nodes.append((o, cstr(raw, o+8), u32(raw, o), u32(raw, o+4)))
        o = u32(raw, o)
    return nodes

path = sys.argv[1]
raw = load(path)
dlist = set(dds_offsets(raw))
nodes = chain(raw)
print(f'{os.path.basename(path)} raw={len(raw)} count={u32(raw,0x20)} chainlen={len(nodes)} dds_blocks={len(dlist)}')

used = set()
for o, name, nxt, prv in nodes:
    found = None
    for p in range(o+8, min(len(raw)-36, o+0x4000)):
        w, h, a, b, c = struct.unpack_from('<5I', raw, p)
        mips = u16(raw, p+20)
        off = u32(raw, p+24)
        fmt = u32(raw, p+28)
        size = u32(raw, p+32)
        if off in dlist and w and h and w <= 4096 and h <= 4096 and 1 <= mips <= 14 and size:
            d = dds_info(raw, off)
            if d['w'] == w and d['h'] == h and d['mips'] == mips:
                found = (p, w, h, a, b, c, mips, off, fmt, size, d)
                break
    if found:
        p, w, h, a, b, c, mips, off, fmt, size, d = found
        used.add(off)
        print(f'{name:26s} node={o:#07x} desc={p:#07x} {w:4d}x{h:<4d} fmt={fmt} a={a} b={b} c={c} mips={mips:2d} off={off:#08x} size={size:7d} fourcc={d["fourcc"]}')
    else:
        print(f'{name:26s} node={o:#07x} DESC NOT FOUND  next={nxt:#x}')
print('unreferenced DDS blocks:', [hex(x) for x in sorted(dlist - used)])
