"""Minimal PNG reader/writer and RGBA image helpers, Python 3 standard library only.

Reads non-interlaced 8-bit PNGs (grey, grey+alpha, RGB, RGBA, palette) and writes 8-bit RGBA. An image is a
list of rows, each row a list of (r, g, b, a) tuples. Everything is deterministic: the same pixels always give the same bytes.
"""
import colorsys
import struct
import zlib

TRANSPARENT = (0, 0, 0, 0)


class Image:
    def __init__(self, width, height, pixels=None):
        self.width = width
        self.height = height
        self.px = pixels if pixels is not None else [[TRANSPARENT] * width for _ in range(height)]

    def copy(self):
        return Image(self.width, self.height, [row[:] for row in self.px])

    def crop(self, x, y, w, h):
        out = Image(w, h)
        for j in range(h):
            for i in range(w):
                sx, sy = x + i, y + j
                if 0 <= sx < self.width and 0 <= sy < self.height:
                    out.px[j][i] = self.px[sy][sx]
        return out

    def paste(self, src, x, y):
        """Alpha-composites src at (x, y); fully opaque or fully transparent pixels only are expected."""
        for j in range(src.height):
            for i in range(src.width):
                p = src.px[j][i]
                dx, dy = x + i, y + j
                if p[3] and 0 <= dx < self.width and 0 <= dy < self.height:
                    self.px[dy][dx] = p

    def flip_h(self):
        return Image(self.width, self.height, [row[::-1] for row in self.px])

    def scale(self, k):
        out = Image(self.width * k, self.height * k)
        for j in range(out.height):
            for i in range(out.width):
                out.px[j][i] = self.px[j // k][i // k]
        return out

    def map_pixels(self, fn):
        return Image(self.width, self.height, [[fn(p) if p[3] else p for p in row] for row in self.px])

    def is_empty(self):
        return all(p[3] == 0 for row in self.px for p in row)

    def bbox(self):
        xs = [i for row in self.px for i, p in enumerate(row) if p[3]]
        ys = [j for j, row in enumerate(self.px) if any(p[3] for p in row)]
        if not xs:
            return None
        return min(xs), min(ys), max(xs) + 1, max(ys) + 1


def recolor(hue_shift=0.0, sat=1.0, val=1.0, only=None):
    """Returns a pixel function: rotates the hue by hue_shift (0..1), scales saturation and value.

    only: optional list of (lo, hi) hue ranges in 0..1 (a range may wrap: lo > hi); pixels outside them are left alone.
    """
    def fn(p):
        r, g, b, a = p
        h, s, v = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
        if only is not None and not any((lo <= h <= hi) if lo <= hi else (h >= lo or h <= hi) for lo, hi in only):
            return p
        h = (h + hue_shift) % 1.0
        s = min(1.0, s * sat)
        v = min(1.0, v * val)
        nr, ng, nb = colorsys.hsv_to_rgb(h, s, v)
        return (round(nr * 255), round(ng * 255), round(nb * 255), a)
    return fn


def read_png(path):
    with open(path, 'rb') as f:
        data = f.read()
    if data[:8] != b'\x89PNG\r\n\x1a\n':
        raise ValueError(path + ': not a PNG')
    pos = 8
    idat = b''
    palette = []
    trns = b''
    while pos < len(data):
        length, kind = struct.unpack('>I4s', data[pos:pos + 8])
        body = data[pos + 8:pos + 8 + length]
        pos += 12 + length
        if kind == b'IHDR':
            width, height, depth, ctype, _, _, interlace = struct.unpack('>IIBBBBB', body)
            if interlace != 0 or not (depth == 8 or (ctype == 3 and depth in (1, 2, 4))):
                raise ValueError(path + ': only non-interlaced 8-bit (or 1/2/4-bit palette) PNGs are supported')
        elif kind == b'PLTE':
            palette = [tuple(body[i:i + 3]) for i in range(0, len(body), 3)]
        elif kind == b'tRNS':
            trns = body
        elif kind == b'IDAT':
            idat += body
        elif kind == b'IEND':
            break
    channels = {0: 1, 2: 3, 3: 1, 4: 2, 6: 4}[ctype]
    raw = zlib.decompress(idat)
    stride = (width * depth + 7) // 8 if depth < 8 else width * channels
    rows = []
    prev = bytearray(stride)
    p = 0
    for _ in range(height):
        ft = raw[p]
        line = bytearray(raw[p + 1:p + 1 + stride])
        p += 1 + stride
        for i in range(stride):
            a = line[i - channels] if i >= channels else 0
            b = prev[i]
            c = prev[i - channels] if i >= channels else 0
            if ft == 1:
                line[i] = (line[i] + a) & 255
            elif ft == 2:
                line[i] = (line[i] + b) & 255
            elif ft == 3:
                line[i] = (line[i] + ((a + b) >> 1)) & 255
            elif ft == 4:
                pa, pb, pc = abs(b - c), abs(a - c), abs(a + b - 2 * c)
                pr = a if pa <= pb and pa <= pc else (b if pb <= pc else c)
                line[i] = (line[i] + pr) & 255
        rows.append(line)
        prev = line
    img = Image(width, height)
    mask = (1 << depth) - 1
    for y, line in enumerate(rows):
        for x in range(width):
            o = x * channels
            if ctype == 6:
                px = (line[o], line[o + 1], line[o + 2], line[o + 3])
            elif ctype == 2:
                px = (line[o], line[o + 1], line[o + 2], 255)
            elif ctype == 0:
                px = (line[o], line[o], line[o], 255)
            elif ctype == 4:
                px = (line[o], line[o], line[o], line[o + 1])
            else:
                idx = line[o] if depth == 8 else (line[(x * depth) >> 3] >> (8 - depth - ((x * depth) & 7))) & mask
                r, g, b = palette[idx]
                px = (r, g, b, trns[idx] if idx < len(trns) else 255)
            img.px[y][x] = px if px[3] else TRANSPARENT
    return img


def write_png(path, img):
    raw = bytearray()
    for row in img.px:
        raw.append(0)
        for r, g, b, a in row:
            raw += bytes((r, g, b, a) if a else (0, 0, 0, 0))

    def chunk(kind, body):
        crc = zlib.crc32(kind + body) & 0xFFFFFFFF
        return struct.pack('>I', len(body)) + kind + body + struct.pack('>I', crc)

    out = b'\x89PNG\r\n\x1a\n'
    out += chunk(b'IHDR', struct.pack('>IIBBBBB', img.width, img.height, 8, 6, 0, 0, 0))
    out += chunk(b'IDAT', zlib.compress(bytes(raw), 9))
    out += chunk(b'IEND', b'')
    with open(path, 'wb') as f:
        f.write(out)
