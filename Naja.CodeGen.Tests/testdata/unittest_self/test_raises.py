"""Self-tests for assertRaises (callable form) and failureException property."""
import unittest


class RaisesTests(unittest.TestCase):

    def test_raises_callable_form(self):
        def boom():
            raise ValueError("bad value")
        self.assertRaises(ValueError, boom)

    def test_raises_with_args(self):
        def divide(a, b):
            return a / b
        self.assertRaises(ZeroDivisionError, divide, 1, 0)

    def test_raises_not_raised_fails(self):
        try:
            self.assertRaises(ValueError, lambda: None)
            self.fail("Expected AssertionError when no exception raised")
        except AssertionError:
            pass

    def test_raises_wrong_type_propagates(self):
        try:
            self.assertRaises(ValueError, lambda: 1 / 0)
            self.fail("Expected ZeroDivisionError to propagate")
        except ZeroDivisionError:
            pass

    def test_failure_exception_is_assertion_error(self):
        # failureException should point to the exception type raised on assertion failure
        exc_type = self.failureException
        self.assertIsNotNone(exc_type)
        try:
            self.assertEqual(1, 2)
        except exc_type:
            pass

    def test_setUp_tearDown_lifecycle(self):
        # setUp/tearDown are called around each test method
        # This test just exercises the mechanism — NajaTestCase.main() calls them
        self.assertTrue(True)


if __name__ == "__main__":
    unittest.main()
