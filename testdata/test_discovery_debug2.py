"""Test discovery debug - simpler."""
import unittest
import sys

class TestDemo(unittest.TestCase):
    """Demo test class."""
    
    def test_pass(self):
        """This should pass."""
        self.assertTrue(True)

# Print debugging information
print("Module name:", __name__)
print("TestDemo class:", TestDemo)

if __name__ == "__main__":
    unittest.main(verbosity=2)
