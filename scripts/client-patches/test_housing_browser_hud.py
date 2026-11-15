import struct
import unittest

from fix_housing_browser_hud import Prototype, Reader, patch, string


HEADER = bytes.fromhex('1b4c75615100010404040800')


def text(value):
    return b'\x04' + string(value.encode() + b'\0')


def browser():
    return Prototype(b'@browser.lua\0', struct.pack('<II4B', 1, 20, 0, 1, 0, 5),
                     [30 | (1 << 23)],
                     [text(key) for key in ('setIconType', 'PopulateItemOfTheDay', 'ShowClickGobbler', 'petSelected')],
                     [], [2], [(b'self\0', 0, 1)], [])


def decode(data):
    reader = Reader(data)
    reader.take(12)
    node = Prototype.read(reader)
    assert reader.position == len(data)
    return node


class BrowserHudPatchTests(unittest.TestCase):
    def test_reset_changes_only_target_entry_and_its_debug_offsets(self):
        root = browser()
        unrelated = browser()
        unrelated.constants = [b'\x00', b'\x01\x01', b'\x03' + struct.pack('<d', 4.5)]
        root.children = [unrelated]
        result = decode(patch(HEADER + root.serialize()))
        self.assertEqual(unrelated.serialize(), result.children[0].serialize())
        self.assertEqual(root.code, result.code[1:])
        instruction = result.code[0]
        self.assertEqual(9, instruction & 63)  # Lua 5.1 SETTABLE.
        self.assertEqual(0, (instruction >> 6) & 255)  # Existing self register.
        self.assertEqual(text('SuppressRestore'), result.constants[((instruction >> 23) & 511) - 256])
        self.assertEqual(b'\x01\x00', result.constants[((instruction >> 14) & 511) - 256])
        self.assertEqual([2, 2], result.lines)
        self.assertEqual([(b'self\0', 0, 2)], result.locals)

    def test_reset_overrides_stale_flag_without_changing_other_ui_state(self):
        result = decode(patch(HEADER + browser().serialize()))
        # The inserted SETTABLE writes only self.SuppressRestore, for both initial states.
        for stale in (True, False):
            ui = {'SuppressRestore': stale, 'visible': False, 'petActive': False}
            instruction = result.code[0]
            field = result.constants[((instruction >> 23) & 511) - 256]
            self.assertEqual(text('SuppressRestore'), field)
            ui['SuppressRestore'] = bool(result.constants[((instruction >> 14) & 511) - 256][1])
            self.assertEqual({'SuppressRestore': False, 'visible': False, 'petActive': False}, ui)

    def test_unknown_format_truncated_missing_and_duplicate_targets_are_rejected(self):
        node = browser()
        original = HEADER + node.serialize()
        with self.assertRaises(ValueError):
            patch(b'bad' + original[3:])
        with self.assertRaises(ValueError):
            patch(original[:-1])
        node.constants.clear()
        with self.assertRaises(ValueError):
            patch(HEADER + node.serialize())
        node = browser()
        node.children.append(browser())
        with self.assertRaises(ValueError):
            patch(HEADER + node.serialize())

    def test_second_application_is_rejected(self):
        with self.assertRaises(ValueError):
            patch(patch(HEADER + browser().serialize()))


if __name__ == '__main__':
    unittest.main()
