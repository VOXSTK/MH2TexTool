import struct, sys, zlib, re, os

def load(path):
    data = open(path, 'rb').read()
    magic = data[:4]
    if magic == b'Z2HM':
        usize = struct.unpack_from('<I', data, 4)[0]
        raw = zlib.decompress(data[8:])
        return 'Z2HM', usize, raw, data
    return 'TCDT', len(data), data, data

def main(path):
    magic, usize, raw, orig = load(path)
    print(f'file      : {os.path.basename(path)}')
    print(f'container : {magic}  stored={len(orig)}  raw={len(raw)}  declared={usize}')
    print(f'raw magic : {raw[:4]}')
    print('--- first 96 bytes ---')
    for i in range(0, 96, 16):
        chunk = raw[i:i+16]
        hexs = ' '.join(f'{b:02X}' for b in chunk)
        asc = ''.join(chr(b) if 32 <= b < 127 else '.' for b in chunk)
        print(f'{i:04X}  {hexs}  {asc}')

    # find ASCII names of length >= 3 that look like texture names
    names = []
    for m in re.finditer(rb'[\x20-\x7E]{3,48}', raw[:4096]):
        s = m.group().decode()
        if re.fullmatch(r'[A-Za-z0-9_\-\.]+', s) and not s.startswith(('TCDT', 'SystemDr', 'USERDNSDOMAI', 'NVTT')):
            names.append((m.start(), s))
    print('--- candidate names in header (first 4KB) ---')
    for off, s in names[:20]:
        print(f'  0x{off:04X}  {s}')

if __name__ == '__main__':
    main(sys.argv[1])
