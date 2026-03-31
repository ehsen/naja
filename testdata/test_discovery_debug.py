"""Test discovery debug."""
import unittest
import sys
import typing

class TestDemo(unittest.TestCase):
    """Demo test class."""
    
    def test_pass(self):
        """This should pass."""
        self.assertTrue(True)

# Print debugging information
print(f"Module name: {__name__}", file=sys.stderr)
print(f"TestDemo class: {TestDemo}", file=sys.stderr)
print(f"TestDemo bases: {TestDemo.__bases__}", file=sys.stderr)
print(f"Is TestDemo a TestCase? {issubclass(TestDemo, unittest.TestCase)}", file=sys.stderr)

if __name__ == "__main__":
    unittest.main(verbosity=2)
