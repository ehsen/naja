"""Self-tests for skipTest and fail()."""
import unittest


class SkipAndFailTests(unittest.TestCase):

    def test_fail_raises_assertion_error(self):
        try:
            self.fail("deliberate failure")
        except AssertionError as e:
            self.assertIn("deliberate failure", str(e))

    def test_fail_no_args(self):
        try:
            self.fail()
        except AssertionError:
            pass

    def test_skip_raises_skip_exception(self):
        # We can't actually skip here (that would skip this test), but we verify
        # that calling skipTest raises an exception.
        raised = False
        try:
            self.skipTest("reason")
        except Exception:
            raised = True
        self.assertTrue(raised)


if __name__ == "__main__":
    unittest.main()
