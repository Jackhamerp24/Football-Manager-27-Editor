# Minimal PE section dump + RVA classification for game_plugin.dll.
import struct, sys

def sections(path):
    with open(path, "rb") as f:
        hdr = f.read(0x1000)
    pe = struct.unpack_from("<I", hdr, 0x3C)[0]
    nsec = struct.unpack_from("<H", hdr, pe + 6)[0]
    optsz = struct.unpack_from("<H", hdr, pe + 20)[0]
    opt = pe + 24
    imagebase = struct.unpack_from("<Q", hdr, opt + 24)[0]
    sizeimg = struct.unpack_from("<I", hdr, opt + 56)[0]
    secs = []
    for i in range(nsec):
        o = opt + optsz + i * 40
        name = hdr[o:o + 8].rstrip(b"\0").decode()
        vsize, va, rsize, raw = struct.unpack_from("<IIII", hdr, o + 8)
        secs.append((name, va, vsize, raw, rsize))
    return imagebase, sizeimg, secs

if __name__ == "__main__":
    path = sys.argv[1]
    ib, si, secs = sections(path)
    print(f"ImageBase=0x{ib:X} SizeOfImage=0x{si:X}")
    for s in secs:
        print(f"  {s[0]:8} va=0x{s[1]:08X} vsize=0x{s[2]:08X} raw=0x{s[3]:08X} rsize=0x{s[4]:08X}")
    for r in sys.argv[2:]:
        rv = int(r, 0)
        hit = [s[0] for s in secs if s[1] <= rv < s[1] + s[2]]
        print(f"  rva 0x{rv:X} -> {hit}")
