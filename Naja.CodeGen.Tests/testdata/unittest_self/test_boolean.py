"""Self-tests for assertTrue / assertFalse / assertIn / assertNotIn."""
import unittest


class BooleanTests(unittest.TestCase):

    def test_true_values(self):
        self.assertTrue(True)
        self.assertTrue(1)
        self.assertTrue("non-empty")
        self.assertTrue([1, 2])

    def test_false_values(self):
        self.assertFalse(False)
        self.assertFalse(0)
        self.assertFalse("")
        self.assertFalse([])

    def test_assert_in_list(self):
        self.assertIn(1, [1, 2, 3])
        self.assertIn("a", ["a", "b"])

    def test_assert_in_string(self):
        self.assertIn("ell", "hello")
        self.assertIn("h", "hello")

    def test_assert_not_in(self):
        self.assertNotIn(99, [1, 2, 3])
        self.assertNotIn("z", "hello")

    def test_true_fails(self):
        try:
            self.assertTrue(False)
            self.fail("Expected AssertionError")
        except AssertionError:
            pass

    def test_false_fails(self):
        try:
            self.assertFalse(True)
            self.fail("Expected AssertionError")
        except AssertionError:
            pass


if __name__ == "__main__":
    unittest.main()
