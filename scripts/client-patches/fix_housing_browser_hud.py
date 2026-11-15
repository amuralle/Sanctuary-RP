"""Reset a stale browser HUD flag without recompiling unrelated client Lua code.

Lua 5.1, x86 client chunk only. Default is an audit. --output writes a new file,
never the input. Validate the output with unluac before deploying it locally.
"""

import argparse
import hashlib
import struct
from dataclasses import dataclass
from pathlib import Path


def integer(value):
    return struct.pack('<I', value)


def string(value):
    return integer(len(value)) + value


class Reader:
    def __init__(self, data):
        self.data = data
        self.position = 0

    def take(self, count):
        end = self.position + count
        if end > len(self.data):
            raise ValueError('Truncated Lua chunk')
        value = self.data[self.position:end]
        self.position = end
        return value

    def integer(self):
        return struct.unpack('<I', self.take(4))[0]

    def string(self):
        return self.take(self.integer())

    def vector(self, read):
        return [read() for _ in range(self.integer())]


@dataclass
class Prototype:
    source: bytes
    header: bytes
    code: list
    constants: list
    children: list
    lines: list
    locals: list
    upvalues: list

    @classmethod
    def read(cls, reader):
        source = reader.string()
        header = reader.take(12)
        code = reader.vector(reader.integer)

        def constant():
            tag = reader.take(1)
            if tag == b'\x00':
                return tag
            if tag == b'\x01':
                return tag + reader.take(1)
            if tag == b'\x03':
                return tag + reader.take(8)
            if tag == b'\x04':
                return tag + string(reader.string())
            raise ValueError(f'Unsupported Lua constant {tag!r}')

        constants = reader.vector(constant)
        children = reader.vector(lambda: cls.read(reader))
        lines = reader.vector(reader.integer)
        locals_ = reader.vector(lambda: (reader.string(), reader.integer(), reader.integer()))
        upvalues = reader.vector(reader.string)
        return cls(source, header, code, constants, children, lines, locals_, upvalues)

    def serialize(self):
        def vector(values, write):
            return integer(len(values)) + b''.join(write(value) for value in values)

        return (string(self.source) + self.header + vector(self.code, integer)
                + vector(self.constants, lambda value: value)
                + vector(self.children, lambda child: child.serialize())
                + vector(self.lines, integer)
                + vector(self.locals, lambda local: string(local[0]) + integer(local[1]) + integer(local[2]))
                + vector(self.upvalues, string))

    def walk(self):
        yield self
        for child in self.children:
            yield from child.walk()


def patch(data):
    # Signature, version, format, endianness, int/size_t/instruction/number sizes, integer-number flag.
    header = bytes.fromhex('1b4c75615100010404040800')
    if data[:12] != header:
        raise ValueError('Expected the original x86 Lua 5.1 client format')
    reader = Reader(data)
    reader.take(12)
    root = Prototype.read(reader)
    if reader.position != len(data) or header + root.serialize() != data:
        raise ValueError('Input did not round-trip exactly')

    def key(value):
        return b'\x04' + string(value.encode('ascii') + b'\0')

    markers = [key(value) for value in ('setIconType', 'PopulateItemOfTheDay', 'ShowClickGobbler', 'petSelected')]
    targets = [node for node in root.walk() if all(marker in node.constants for marker in markers)]
    if len(targets) != 1:
        raise ValueError(f'Expected one BrowserV2.ShowWindow, found {len(targets)}')
    target = targets[0]
    if key('SuppressRestore') in target.constants:
        raise ValueError('Browser already references SuppressRestore; inspect instead of patching twice')
    target.constants.append(key('SuppressRestore'))
    field = len(target.constants) - 1
    false = b'\x01\x00'
    if false not in target.constants:
        target.constants.append(false)
    value = target.constants.index(false)
    if max(field, value) >= 256 or target.header[9] < 1:
        raise ValueError('Cannot encode the reset with existing Lua registers')

    # SETTABLE R0, K[SuppressRestore], K[false]; R0 is the existing method's self.
    # All existing relative jumps stay valid because the entire body shifts equally.
    target.code.insert(0, 9 | ((256 + field) << 23) | ((256 + value) << 14))
    if target.lines:
        target.lines.insert(0, target.lines[0])
    target.locals = [(name, start + (start > 0), end + 1) for name, start, end in target.locals]
    return header + root.serialize()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path)
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    data = args.source.read_bytes()
    result = patch(data)
    print(f'Input SHA256: {hashlib.sha256(data).hexdigest()}')
    print(f'Output SHA256: {hashlib.sha256(result).hexdigest()}')
    print(f'Change: one method-entry reset; size {len(data)} -> {len(result)} bytes')
    if args.output:
        if args.output.resolve() == args.source.resolve():
            parser.error('Never overwrite the source chunk')
        with args.output.open('xb') as output:
            output.write(result)


if __name__ == '__main__':
    main()
