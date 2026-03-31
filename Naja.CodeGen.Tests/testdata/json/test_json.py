"""
Comprehensive JSON module tests, replicated from CPython's test_json without dependencies.

Tests cover:
- Basic serialization (dumps) and deserialization (loads)
- Data type conversions (None, bool, int, float, str, list, dict)
- Indentation and formatting options
- Key sorting
- Unicode handling (ensure_ascii)
- Edge cases and error handling
- NaN/Infinity handling
- Circular reference detection (conceptual)
"""

import json
import unittest


class TestJsonBasic(unittest.TestCase):
    """Basic JSON serialization and deserialization tests."""

    def test_dumps_none(self):
        """json.dumps(None) == 'null'"""
        self.assertEqual(json.dumps(None), 'null')

    def test_dumps_bool_true(self):
        """json.dumps(True) == 'true'"""
        self.assertEqual(json.dumps(True), 'true')

    def test_dumps_bool_false(self):
        """json.dumps(False) == 'false'"""
        self.assertEqual(json.dumps(False), 'false')

    def test_dumps_int_zero(self):
        """json.dumps(0) == '0'"""
        self.assertEqual(json.dumps(0), '0')

    def test_dumps_int_positive(self):
        """json.dumps(42) == '42'"""
        self.assertEqual(json.dumps(42), '42')

    def test_dumps_int_negative(self):
        """json.dumps(-42) == '-42'"""
        self.assertEqual(json.dumps(-42), '-42')

    def test_dumps_int_large(self):
        """json.dumps(9223372036854775807) == '9223372036854775807'"""
        self.assertEqual(json.dumps(9223372036854775807), '9223372036854775807')

    def test_dumps_float_positive(self):
        """json.dumps(3.14) includes decimal point"""
        result = json.dumps(3.14)
        self.assertIn('.', result)

    def test_dumps_float_negative(self):
        """json.dumps(-2.5) includes decimal point"""
        result = json.dumps(-2.5)
        self.assertIn('.', result)

    def test_dumps_float_zero(self):
        """json.dumps(0.0) == '0.0'"""
        self.assertEqual(json.dumps(0.0), '0.0')

    def test_dumps_float_exponential(self):
        """json.dumps(1e10) produces exponential notation"""
        result = json.dumps(1e10)
        # Could be '10000000000.0' or '1e+10' depending on implementation
        val = json.loads(result)
        self.assertEqual(val, 1e10)

    def test_dumps_string_empty(self):
        """json.dumps('') == '\\\"\\\"'"""
        self.assertEqual(json.dumps(''), '""')

    def test_dumps_string_simple(self):
        """json.dumps('hello') == '\\\"hello\\\"'"""
        self.assertEqual(json.dumps('hello'), '"hello"')

    def test_dumps_string_with_escapes(self):
        """json.dumps with escape sequences"""
        self.assertEqual(json.dumps('hello"world'), '"hello\\"world"')
        self.assertEqual(json.dumps('line1\nline2'), '"line1\\nline2"')
        self.assertEqual(json.dumps('tab\there'), '"tab\\there"')

    def test_dumps_string_with_backslash(self):
        """json.dumps with backslash escaping"""
        self.assertEqual(json.dumps('a\\b'), '"a\\\\b"')

    def test_dumps_list_empty(self):
        """json.dumps([]) == '[]'"""
        self.assertEqual(json.dumps([]), '[]')

    def test_dumps_list_simple(self):
        """json.dumps([1, 2, 3]) == '[1, 2, 3]'"""
        self.assertEqual(json.dumps([1, 2, 3]), '[1, 2, 3]')

    def test_dumps_list_mixed(self):
        """json.dumps with mixed types in list"""
        result = json.dumps([1, 'hello', True, None])
        self.assertEqual(result, '[1, "hello", true, null]')

    def test_dumps_dict_empty(self):
        """json.dumps({}) == '{}'"""
        self.assertEqual(json.dumps({}), '{}')

    def test_dumps_dict_simple(self):
        """json.dumps({'key': 'value'}) creates JSON object"""
        result = json.dumps({'key': 'value'})
        self.assertEqual(result, '{"key": "value"}')

    def test_dumps_dict_int_key_converted(self):
        """json.dumps with int keys converts them to strings"""
        result = json.dumps({1: 'one', 2: 'two'})
        # Keys are strings in JSON
        parsed = json.loads(result)
        self.assertEqual(parsed['1'], 'one')
        self.assertEqual(parsed['2'], 'two')

    def test_dumps_nested_structure(self):
        """json.dumps with nested lists and dicts"""
        obj = {'items': [1, 2, {'nested': True}], 'count': 3}
        result = json.dumps(obj)
        parsed = json.loads(result)
        self.assertEqual(parsed['items'][2]['nested'], True)

    def test_loads_null(self):
        """json.loads('null') == None"""
        self.assertIsNone(json.loads('null'))

    def test_loads_bool_true(self):
        """json.loads('true') == True"""
        self.assertIs(json.loads('true'), True)

    def test_loads_bool_false(self):
        """json.loads('false') == False"""
        self.assertIs(json.loads('false'), False)

    def test_loads_int(self):
        """json.loads('42') == 42"""
        self.assertEqual(json.loads('42'), 42)

    def test_loads_float(self):
        """json.loads('3.14') == 3.14"""
        self.assertEqual(json.loads('3.14'), 3.14)

    def test_loads_string(self):
        """json.loads('\\\"hello\\\"') == 'hello'"""
        self.assertEqual(json.loads('"hello"'), 'hello')

    def test_loads_string_with_escapes(self):
        """json.loads with escape sequences"""
        self.assertEqual(json.loads('"hello\\"world"'), 'hello"world')
        self.assertEqual(json.loads('"line1\\nline2"'), 'line1\nline2')

    def test_loads_list(self):
        """json.loads('[1, 2, 3]') == [1, 2, 3]"""
        self.assertEqual(json.loads('[1, 2, 3]'), [1, 2, 3])

    def test_loads_dict(self):
        """json.loads('{\\\"key\\\": \\\"value\\\"}') creates dict"""
        result = json.loads('{"key": "value"}')
        self.assertEqual(result['key'], 'value')

    def test_loads_nested(self):
        """json.loads with nested structures"""
        json_str = '{"items": [1, 2, {"nested": true}], "count": 3}'
        result = json.loads(json_str)
        self.assertEqual(result['items'][2]['nested'], True)

    def test_roundtrip_basic_types(self):
        """json.dumps -> json.loads roundtrip for basic types"""
        values = [None, True, False, 0, 42, -17, 3.14, '', 'hello', [], {}]
        for val in values:
            dumped = json.dumps(val)
            loaded = json.loads(dumped)
            self.assertEqual(loaded, val)

    def test_roundtrip_nested(self):
        """json.dumps -> json.loads roundtrip for nested structures"""
        obj = {
            'string': 'hello',
            'number': 42,
            'float': 3.14,
            'bool': True,
            'null': None,
            'list': [1, 2, 3],
            'nested': {'a': 1, 'b': 2}
        }
        dumped = json.dumps(obj)
        loaded = json.loads(dumped)
        self.assertEqual(loaded, obj)


class TestJsonIndent(unittest.TestCase):
    """Tests for indentation formatting."""

    def test_dumps_indent_none_compact(self):
        """json.dumps(indent=None) produces compact output"""
        obj = {'key': 'value', 'list': [1, 2]}
        result = json.dumps(obj, indent=None)
        # No newlines in compact mode
        self.assertNotIn('\n', result)

    def test_dumps_indent_2(self):
        """json.dumps(indent=2) indents with 2 spaces"""
        obj = {'key': 'value', 'list': [1, 2]}
        result = json.dumps(obj, indent=2)
        self.assertIn('\n', result)
        # Should have 2-space indentation
        self.assertIn('  ', result)

    def test_dumps_indent_4(self):
        """json.dumps(indent=4) indents with 4 spaces"""
        obj = {'a': 1}
        result = json.dumps(obj, indent=4)
        self.assertIn('\n', result)

    def test_dumps_indent_nested(self):
        """json.dumps with indent on nested structures"""
        obj = {'outer': {'inner': [1, 2, 3]}}
        result = json.dumps(obj, indent=2)
        # Should be able to parse back
        parsed = json.loads(result)
        self.assertEqual(parsed, obj)

    def test_dumps_indent_empty_list(self):
        """json.dumps([], indent=2) should not add newlines"""
        result = json.dumps([], indent=2)
        self.assertEqual(result, '[]')

    def test_dumps_indent_empty_dict(self):
        """json.dumps({}, indent=2) should not add newlines"""
        result = json.dumps({}, indent=2)
        self.assertEqual(result, '{}')


class TestJsonSortKeys(unittest.TestCase):
    """Tests for key sorting."""

    def test_dumps_sort_keys_false(self):
        """json.dumps(sort_keys=False) preserves insertion order"""
        obj = {'z': 1, 'a': 2, 'm': 3}
        result = json.dumps(obj, sort_keys=False)
        parsed = json.loads(result)
        self.assertEqual(parsed, obj)

    def test_dumps_sort_keys_true(self):
        """json.dumps(sort_keys=True) sorts keys alphabetically"""
        obj = {'z': 1, 'a': 2, 'm': 3}
        result = json.dumps(obj, sort_keys=True)
        # Check that 'a' comes before 'm' comes before 'z'
        a_pos = result.index('"a"')
        m_pos = result.index('"m"')
        z_pos = result.index('"z"')
        self.assertTrue(a_pos < m_pos < z_pos)

    def test_dumps_sort_keys_with_indent(self):
        """json.dumps with both sort_keys=True and indent"""
        obj = {'z': 1, 'a': 2}
        result = json.dumps(obj, sort_keys=True, indent=2)
        parsed = json.loads(result)
        self.assertEqual(parsed, obj)


class TestJsonEnsureAscii(unittest.TestCase):
    """Tests for ensure_ascii option."""

    def test_dumps_ensure_ascii_true(self):
        """json.dumps(ensure_ascii=True) escapes non-ASCII"""
        obj = {'name': 'café'}
        result = json.dumps(obj, ensure_ascii=True)
        # Non-ASCII characters should be escaped as \\uXXXX
        self.assertNotIn('é', result)
        self.assertIn('\\u', result)

    def test_dumps_ensure_ascii_false(self):
        """json.dumps(ensure_ascii=False) preserves non-ASCII"""
        obj = {'name': 'café'}
        result = json.dumps(obj, ensure_ascii=False)
        # Non-ASCII characters should be preserved
        self.assertIn('é', result)

    def test_ensure_ascii_default_true(self):
        """ensure_ascii defaults to True"""
        obj = {'text': 'café'}
        result = json.dumps(obj)
        # Should escape by default
        self.assertNotIn('é', result)


class TestJsonErrors(unittest.TestCase):
    """Tests for error handling."""

    def test_loads_invalid_json(self):
        """json.loads with invalid JSON raises ValueError"""
        with self.assertRaises(ValueError):
            json.loads('{invalid}')

    def test_loads_trailing_data(self):
        """json.loads with trailing data after valid JSON"""
        with self.assertRaises(ValueError):
            json.loads('[] extra')

    def test_loads_malformed_array(self):
        """json.loads with malformed array"""
        with self.assertRaises(ValueError):
            json.loads('[1, 2,')

    def test_loads_malformed_object(self):
        """json.loads with malformed object"""
        with self.assertRaises(ValueError):
            json.loads('{"key": "value"')

    def test_loads_unquoted_string(self):
        """json.loads with unquoted string raises error"""
        with self.assertRaises(ValueError):
            json.loads('hello')

    def test_dumps_non_serializable(self):
        """json.dumps with non-serializable object raises TypeError"""
        class CustomObj:
            pass
        with self.assertRaises(TypeError):
            json.dumps(CustomObj())

    def test_dumps_dict_non_string_key_no_default(self):
        """json.dumps dict with non-string key raises TypeError"""
        # When int key cannot be converted
        with self.assertRaises(TypeError):
            json.dumps({(1, 2): 'tuple_key'})

    def test_dumps_nan_raises(self):
        """json.dumps with NaN raises ValueError"""
        with self.assertRaises(ValueError):
            json.dumps(float('nan'))

    def test_dumps_infinity_raises(self):
        """json.dumps with Infinity raises ValueError"""
        with self.assertRaises(ValueError):
            json.dumps(float('inf'))

    def test_dumps_negative_infinity_raises(self):
        """json.dumps with -Infinity raises ValueError"""
        with self.assertRaises(ValueError):
            json.dumps(float('-inf'))


class TestJsonUnicode(unittest.TestCase):
    """Tests for Unicode handling."""

    def test_unicode_string(self):
        """json.dumps/loads preserves Unicode strings"""
        text = 'Hello \u4e2d\u6587 \u0436\u0435\u043b\u0435\u0437\u043d\u0430\u044f'
        dumped = json.dumps(text)
        loaded = json.loads(dumped)
        self.assertEqual(loaded, text)

    def test_unicode_dict_keys(self):
        """json.dumps/loads with Unicode keys"""
        obj = {'\u4e2d\u6587': 'Chinese', '\u0440\u0443\u0441\u0441\u043a\u0438\u0439': 'Russian'}
        dumped = json.dumps(obj)
        loaded = json.loads(dumped)
        self.assertEqual(loaded, obj)

    def test_unicode_in_list(self):
        """json.dumps/loads with Unicode in lists"""
        obj = ['hello', '\u4e2d\u6587', '\ud83d\ude00']  # emoji
        dumped = json.dumps(obj)
        loaded = json.loads(dumped)
        self.assertEqual(loaded, obj)


class TestJsonNumbers(unittest.TestCase):
    """Tests for number handling."""

    def test_int_type_preserved(self):
        """json.loads preserves integers as int, not float"""
        result = json.loads('42')
        self.assertIsInstance(result, int)
        self.assertEqual(result, 42)

    def test_float_with_decimal_point(self):
        """json.loads with decimal point creates float"""
        result = json.loads('42.0')
        self.assertIsInstance(result, float)
        self.assertEqual(result, 42.0)

    def test_float_with_exponent(self):
        """json.loads with exponent notation"""
        result = json.loads('1e10')
        self.assertEqual(result, 1e10)

    def test_negative_numbers(self):
        """json.loads with negative numbers"""
        self.assertEqual(json.loads('-42'), -42)
        self.assertEqual(json.loads('-3.14'), -3.14)

    def test_leading_zeros_invalid(self):
        """json.loads rejects leading zeros (strict JSON)"""
        # Note: CPython json module is strict about this
        with self.assertRaises(ValueError):
            json.loads('01')

    def test_zero_variants(self):
        """json.loads handles zero correctly"""
        self.assertEqual(json.loads('0'), 0)
        self.assertEqual(json.loads('-0'), 0)


class TestJsonWhitespace(unittest.TestCase):
    """Tests for whitespace handling."""

    def test_leading_whitespace(self):
        """json.loads ignores leading whitespace"""
        self.assertEqual(json.loads('   42'), 42)

    def test_trailing_whitespace(self):
        """json.loads ignores trailing whitespace"""
        self.assertEqual(json.loads('42   '), 42)

    def test_internal_whitespace(self):
        """json.loads handles internal whitespace"""
        result = json.loads('[ 1 , 2 , 3 ]')
        self.assertEqual(result, [1, 2, 3])

    def test_whitespace_in_object(self):
        """json.loads handles whitespace in objects"""
        result = json.loads('{ "key" : "value" }')
        self.assertEqual(result['key'], 'value')

    def test_newlines_in_json(self):
        """json.loads handles newlines in JSON"""
        json_str = '''{
  "key": "value",
  "number": 42
}'''
        result = json.loads(json_str)
        self.assertEqual(result['key'], 'value')
        self.assertEqual(result['number'], 42)


class TestJsonEdgeCases(unittest.TestCase):
    """Tests for edge cases."""

    def test_very_nested_structure(self):
        """json.dumps/loads with deeply nested structures"""
        obj = {'a': {'b': {'c': {'d': {'e': {'f': 'deep'}}}}}}
        dumped = json.dumps(obj)
        loaded = json.loads(dumped)
        self.assertEqual(loaded, obj)

    def test_large_list(self):
        """json.dumps/loads with large list"""
        obj = list(range(1000))
        dumped = json.dumps(obj)
        loaded = json.loads(dumped)
        self.assertEqual(loaded, obj)

    def test_large_dict(self):
        """json.dumps/loads with large dict"""
        obj = {str(i): i for i in range(100)}
        dumped = json.dumps(obj)
        loaded = json.loads(dumped)
        self.assertEqual(loaded, obj)

    def test_empty_string_in_list(self):
        """json.dumps/loads with empty strings"""
        obj = ['', 'a', '']
        dumped = json.dumps(obj)
        loaded = json.loads(dumped)
        self.assertEqual(loaded, obj)

    def test_list_of_dicts(self):
        """json.dumps/loads with list of dicts (common pattern)"""
        obj = [
            {'id': 1, 'name': 'Alice'},
            {'id': 2, 'name': 'Bob'},
            {'id': 3, 'name': 'Charlie'}
        ]
        dumped = json.dumps(obj, sort_keys=True)
        loaded = json.loads(dumped)
        self.assertEqual(loaded, obj)

    def test_dict_of_lists(self):
        """json.dumps/loads with dict of lists"""
        obj = {
            'numbers': [1, 2, 3],
            'strings': ['a', 'b', 'c'],
            'booleans': [True, False, True]
        }
        dumped = json.dumps(obj)
        loaded = json.loads(dumped)
        self.assertEqual(loaded, obj)


class TestJsonStringEscapes(unittest.TestCase):
    """Tests for string escape sequences."""

    def test_quote_escape(self):
        """JSON escapes double quotes"""
        obj = 'say "hello"'
        dumped = json.dumps(obj)
        loaded = json.loads(dumped)
        self.assertEqual(loaded, obj)

    def test_backslash_escape(self):
        """JSON escapes backslashes"""
        obj = 'C:\\Users\\Name'
        dumped = json.dumps(obj)
        loaded = json.loads(dumped)
        self.assertEqual(loaded, obj)

    def test_control_char_escape_newline(self):
        """JSON escapes newline"""
        obj = 'line1\nline2'
        dumped = json.dumps(obj)
        loaded = json.loads(dumped)
        self.assertEqual(loaded, obj)

    def test_control_char_escape_tab(self):
        """JSON escapes tab"""
        obj = 'col1\tcol2'
        dumped = json.dumps(obj)
        loaded = json.loads(dumped)
        self.assertEqual(loaded, obj)

    def test_control_char_escape_carriage_return(self):
        """JSON escapes carriage return"""
        obj = 'line1\rline2'
        dumped = json.dumps(obj)
        loaded = json.loads(dumped)
        self.assertEqual(loaded, obj)

    def test_control_char_escape_backspace(self):
        """JSON escapes backspace"""
        obj = 'back\bspace'
        dumped = json.dumps(obj)
        loaded = json.loads(dumped)
        self.assertEqual(loaded, obj)

    def test_control_char_escape_formfeed(self):
        """JSON escapes form feed"""
        obj = 'form\ffeed'
        dumped = json.dumps(obj)
        loaded = json.loads(dumped)
        self.assertEqual(loaded, obj)


class TestJsonTypeConversions(unittest.TestCase):
    """Tests for type conversion and coercion."""

    def test_dict_int_keys_converted_to_str(self):
        """Dict with int keys: keys converted to strings in JSON"""
        obj = {1: 'one', 2: 'two', 3: 'three'}
        dumped = json.dumps(obj)
        loaded = json.loads(dumped)
        # JSON requires string keys
        self.assertIn('one', loaded.values())
        self.assertIn('two', loaded.values())

    def test_dict_bool_key(self):
        """Dict with bool key converted to string"""
        obj = {True: 'yes', False: 'no'}
        dumped = json.dumps(obj)
        loaded = json.loads(dumped)
        # Bool keys become 'true'/'false' strings
        self.assertIn('yes', loaded.values())
        self.assertIn('no', loaded.values())

    def test_dict_float_key(self):
        """Dict with float key converted to string"""
        obj = {3.14: 'pi'}
        dumped = json.dumps(obj)
        loaded = json.loads(dumped)
        # Float key becomes string representation
        self.assertIn('pi', loaded.values())


class TestJsonSimpleTypes(unittest.TestCase):
    """Simple isolated tests for each type."""

    def test_null_roundtrip(self):
        """null <-> None roundtrip"""
        self.assertEqual(json.loads(json.dumps(None)), None)

    def test_true_roundtrip(self):
        """true <-> True roundtrip"""
        self.assertIs(json.loads(json.dumps(True)), True)

    def test_false_roundtrip(self):
        """false <-> False roundtrip"""
        self.assertIs(json.loads(json.dumps(False)), False)

    def test_int_roundtrip(self):
        """int roundtrip"""
        self.assertEqual(json.loads(json.dumps(123)), 123)

    def test_float_roundtrip(self):
        """float roundtrip (approximately)"""
        val = 3.14
        dumped = json.dumps(val)
        loaded = json.loads(dumped)
        self.assertAlmostEqual(loaded, val)

    def test_string_roundtrip(self):
        """string roundtrip"""
        self.assertEqual(json.loads(json.dumps('test')), 'test')

    def test_list_roundtrip(self):
        """list roundtrip"""
        self.assertEqual(json.loads(json.dumps([1, 2, 3])), [1, 2, 3])

    def test_dict_roundtrip(self):
        """dict roundtrip"""
        obj = {'a': 1, 'b': 2}
        self.assertEqual(json.loads(json.dumps(obj)), obj)


if __name__ == '__main__':
    unittest.main()
