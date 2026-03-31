"""Simple test to debug unittest discovery."""
import unittest
import sys

class SimpleTest(unittest.TestCase):
    def test_simple(self):
        """Simple test that should pass."""
        self.assertTrue(True)
    
    def test_another(self):
        """Another test."""
        self.assertEqual(1, 1)

if __name__ == "__main__":
    print("About to run unittest.main()")
    print(f"Module name: {__name__}")
    print(f"Python version: {sys.version}")
    unittest.main()
