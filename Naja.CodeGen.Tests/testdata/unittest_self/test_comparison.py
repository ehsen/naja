"""Self-tests for assertGreater / assertLess / assertGreaterEqual / assertLessEqual
   and assertAlmostEqual / assertNotAlmostEqual."""
import unittest


class ComparisonTests(unittest.TestCase):

    def test_greater(self):
        self.assertGreater(5, 3)
        self.assertGreater(1.0, 0.5)

    def test_less(self):
        self.assertLess(3, 5)
        self.assertLess(0.1, 1.0)

    def test_greater_equal(self):
        self.assertGreaterEqual(5, 5)
        self.assertGreaterEqual(6, 5)

    def test_less_equal(self):
        self.assertLessEqual(5, 5)
        self.assertLessEqual(4, 5)

    def test_almost_equal_default_places(self):
        self.assertAlmostEqual(1.00000001, 1.0)
        self.assertNotAlmostEqual(1.0000001, 1.0)

    def test_almost_equal_places(self):
        self.assertAlmostEqual(1.1, 1.0, 0)
        self.assertNotAlmostEqual(1.1, 1.0, 1)

    def test_greater_fails(self):
        try:
            self.assertGreater(3, 5)
            self.fail("Expected AssertionError")
        except AssertionError:
            pass

    def test_less_fails(self):
        try:
            self.assertLess(5, 3)
            self.fail("Expected AssertionError")
        except AssertionError:
            pass


if __name__ == "__main__":
    unittest.main()
