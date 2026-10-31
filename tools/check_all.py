import struct, sys, zlib, os

def load(path):
    data = open(path, 'rb').read()
    if data[:4] == b'Z2HM':
        return 'Z2HM', zlib.decompress(data[8:])
    return 'TCDT', data

def u32(b, o): return struct.unpack_from('<I', b, o)[0]

def block_size(w, h, bpp, mips, blk):
    n = 0
    for i in range(max(mips, 1)):
        mw = max(1, w >> i); mh = max(1, h >> i)
        if blk == 1:  # uncompressed
            n += mw * mh * bpp // 8
        else:
            n += max(1, (mw + 3) // 4) * max(1, (mh + 3) // 4) * blk
    return n

def check(path):
    kind, raw = load(path)
    count = u32(raw, 0x20)
    sentinel = 0x24
    o = u32(raw, sentinel)
    errs = []
    names = []
    seen = set()
    while o != sentinel and o not in seen:
        seen.add(o)
        if o + 0x80 > len(raw):
            errs.append(f'node {o:#x} out of range'); break
        name = raw[o+8:o+0x48].split(b'\0')[0].decode('latin1')
        w, h, bpp, pitch, blk = struct.unpack_from('<5I', raw, o+0x48)
        mips = struct.unpack_from('<H', raw, o+0x5C)[0]
        doff = u32(raw, o+0x60)
        fmt = raw[o+0x64]
        size = u32(raw, o+0x68)
        names.append(name)
        if raw[doff:doff+4] != b'DDS ':
            errs.append(f'{name}: data off {doff:#x} not DDS'); o = u32(raw, o); continue
        dsize, dflags, dh, dw, dpitch, ddepth, dmips = struct.unpack_from('<7I', raw, doff+4)
        fourcc = raw[doff+0x54:doff+0x58].decode('latin1')
        if (dw, dh) != (w, h):
            errs.append(f'{name}: desc {w}x{h} vs dds {dw}x{dh}')
        if dmips != mips:
            errs.append(f'{name}: desc mips {mips} vs dds {dmips}')
        if pitch != max(16, w * bpp // 8):
            errs.append(f'{name}: pitch {pitch} != max(16, w*bpp/8) {max(16, w*bpp//8)}')
        exp = 128 + block_size(w, h, bpp, mips, blk)
        if size != exp:
            errs.append(f'{name}: size {size} != expected {exp} (diff {size-exp})')
        if raw[doff+size-8:doff+size] != b'' and fourcc == 'DXT1':
            pass
        o = u32(raw, o)
    print(f'{os.path.basename(path):34s} {kind} raw={len(raw):8d} count={count:3d} chain={len(seen):3d} errors={len(errs)}')
    for e in errs[:12]:
        print('   ', e)
    return kind, raw, names

for p in sys.argv[1:]:
    check(p)
