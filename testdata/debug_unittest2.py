"""Debug unittest discovery."""
import unittest
import sys

class SimpleTest(unittest.TestCase):
    def test_simple(self):
        """Simple test that should pass."""
        self.assertTrue(True)

if __name__ == "__main__":
    # Print debug info
    print(f"Script __name__: {__name__}")
    print(f"Module: {__name__}")
    
    # Print assembly info
    import ctypes
    print(f"Loaded assemblies: {ctypes}")
    
    # Run tests
    unittest.main(verbosity=2)
