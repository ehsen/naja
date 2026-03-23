"""Self-tests for assertEqual / assertNotEqual / assertIs / assertIsNot / assertIsNone."""
import unittest


class EqualityTests(unittest.TestCase):

    def test_equal_ints(self):
        self.assertEqual(1, 1)
        self.assertEqual(0, 0)
        self.assertEqual(-5, -5)

    def test_equal_strings(self):
        self.assertEqual("hello", "hello")
        self.assertEqual("", "")

    def test_equal_floats(self):
        self.assertEqual(3.14, 3.14)

    def test_equal_int_float(self):
        self.assertEqual(1, 1.0)
        self.assertEqual(0, 0.0)

    def test_not_equal(self):
        self.assertNotEqual(1, 2)
        self.assertNotEqual("a", "b")
        self.assertNotEqual(1, 2.0)

    def test_assert_is_none(self):
        self.assertIsNone(None)

    def test_assert_is_not_none(self):
        self.assertIsNotNone(0)
        self.assertIsNotNone("")
        self.assertIsNotNone(False)

    def test_assert_is(self):
        x = "same"
        self.assertIs(x, x)

    def test_assert_is_not(self):
        self.assertIsNot("a", "b")

    def test_equal_fails_gives_message(self):
        try:
            self.assertEqual(1, 2)
            self.fail("Expected AssertionError was not raised")
        except AssertionError:
            pass

    def test_not_equal_fails_gives_message(self):
        try:
            self.assertNotEqual(1, 1)
            self.fail("Expected AssertionError was not raised")
        except AssertionError:
            pass


if __name__ == "__main__":
    unittest.main()
