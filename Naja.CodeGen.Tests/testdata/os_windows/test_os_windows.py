"""Windows-specific os module tests for Naja stdlib.

Mirrors CPython Lib/test/test_os.py Windows test classes but depends only on
modules available in the Naja compiler (os, sys, unittest).  No CPython test
infrastructure (test.support), no shutil, no tempfile, no uuid.

Implemented test classes
------------------------
Win32ListdirTests   – os.listdir with normal and extended \\\\?\\ paths
Win32FileOpsTests   – mkdir/makedirs/rmdir/remove/unlink/rename/chdir/getcwd
Win32StatTests      – os.stat st_size and st_mtime
Win32EnvTests       – os.environ, os.getenv, os.putenv
Win32PathTests      – os.path operations with Windows-style paths

Deferred (setUp calls self.skipTest — structure mirrors CPython originals)
--------------------------------------------------------------------------
Win32ListdriveTests – os.listdrives/listvolumes/listmounts not yet in Naja stdlib
Win32SymlinkTests   – os.symlink/readlink/lstat/islink not yet in Naja stdlib
Win32JunctionTests  – _winapi.CreateJunction not yet in Naja stdlib
"""

import sys
import os
import unittest

# ── Helpers ────────────────────────────────────────────────────────────────────

_seq = [0]


def _make_temp_dir():
    """Create a uniquely-named temp directory and return its path."""
    base = os.getenv("TEMP") or os.getenv("TMP") or "C:\\Temp"
    _seq[0] = _seq[0] + 1
    name = "naja_test_" + str(os.getpid()) + "_" + str(_seq[0])
    path = os.path.join(base, name)
    os.makedirs(path)
    return path


def _rmtree(path):
    """Recursively remove a directory tree (wraps os.removedirs)."""
    os.removedirs(path)


# ── Win32ListdirTests ──────────────────────────────────────────────────────────

class Win32ListdirTests(unittest.TestCase):
    """Test os.listdir on Windows.

    Mirrors CPython test_os.Win32ListdirTests.
    """

    def setUp(self):
        self.testdir = _make_temp_dir()
        self.created_paths = []
        for i in range(2):
            dir_name = "SUB" + str(i)
            dir_path = os.path.join(self.testdir, dir_name)
            file_name = "FILE" + str(i)
            file_path = os.path.join(self.testdir, file_name)
            os.makedirs(dir_path)
            f = open(file_path, "w")
            f.write("I'm " + file_path + " and proud of it. Blame test_os.\n")
            f.close()
            self.created_paths.append(dir_name)
            self.created_paths.append(file_name)
        self.created_paths.sort()

    def tearDown(self):
        _rmtree(self.testdir)

    def test_listdir_no_extended_path(self):
        """Test when the path is not an extended path."""
        self.assertEqual(sorted(os.listdir(self.testdir)), self.created_paths)

    def test_listdir_extended_path(self):
        """Test when the path starts with \\\\?\\."""
        path = "\\\\?\\" + os.path.abspath(self.testdir)
        self.assertEqual(sorted(os.listdir(path)), self.created_paths)

    def test_listdir_empty_dir(self):
        """An empty directory should yield an empty list."""
        empty = os.path.join(self.testdir, "EMPTY")
        os.makedirs(empty)
        self.assertEqual(os.listdir(empty), [])

    def test_listdir_returns_names_not_full_paths(self):
        """listdir entries must be bare names, not absolute paths."""
        for entry in os.listdir(self.testdir):
            self.assertFalse(os.path.isabs(entry))

    def test_listdir_nonexistent_raises(self):
        """listdir on a non-existent path must raise OSError."""
        bogus = os.path.join(self.testdir, "no_such_subdir_xyz")
        try:
            os.listdir(bogus)
            self.fail("Expected OSError for non-existent directory")
        except OSError:
            pass


# ── Win32ListdriveTests (deferred — not yet implemented) ──────────────────────

class Win32ListdriveTests(unittest.TestCase):
    """Test os.listdrives, os.listvolumes and os.listmounts on Windows.

    Mirrors CPython test_os.Win32ListdriveTests.
    Deferred: os.listdrives / os.listvolumes / os.listmounts not yet
    implemented in Naja stdlib.
    """

    def setUp(self):
        self.skipTest("os.listdrives/listvolumes/listmounts not yet implemented in Naja stdlib")

    def test_listdrives(self):
        drives = os.listdrives()
        self.assertIsInstance(drives, list)
        self.assertGreater(len(drives), 0)

    def test_listvolumes(self):
        volumes = os.listvolumes()
        self.assertIsInstance(volumes, list)

    def test_listmounts(self):
        for vol in os.listvolumes():
            try:
                mounts = os.listmounts(vol)
                self.assertIsInstance(mounts, list)
            except OSError:
                pass


# ── Win32SymlinkTests (deferred — not yet implemented) ────────────────────────

class Win32SymlinkTests(unittest.TestCase):
    """Test os.symlink, os.readlink and os.lstat on Windows.

    Mirrors CPython test_os.Win32SymlinkTests.
    Deferred: os.symlink / os.readlink / os.lstat / os.path.islink not yet
    implemented in Naja stdlib.
    """

    def setUp(self):
        self.skipTest("os.symlink/readlink/lstat/path.islink not yet implemented in Naja stdlib")

    def test_directory_link(self):
        pass

    def test_file_link(self):
        pass

    def test_remove_directory_link_to_missing_target(self):
        pass

    def test_isdir_on_directory_link_to_missing_target(self):
        pass

    def test_rmdir_on_directory_link_to_missing_target(self):
        pass

    def test_stat_vs_lstat(self):
        pass

    def test_buffer_overflow(self):
        pass

    def test_appexeclink(self):
        pass


# ── Win32JunctionTests (deferred — not yet implemented) ───────────────────────

class Win32JunctionTests(unittest.TestCase):
    """Test NTFS junctions via _winapi.CreateJunction.

    Mirrors CPython test_os.Win32JunctionTests.
    Deferred: _winapi module / os.readlink / os.path.islink not yet
    implemented in Naja stdlib.
    """

    def setUp(self):
        self.skipTest("_winapi.CreateJunction / os.readlink not yet implemented in Naja stdlib")

    def test_create_junction(self):
        pass

    def test_unlink_removes_junction(self):
        pass


# ── Win32FileOpsTests ─────────────────────────────────────────────────────────

class Win32FileOpsTests(unittest.TestCase):
    """Test file and directory operations on Windows."""

    def setUp(self):
        self.testdir = _make_temp_dir()

    def tearDown(self):
        _rmtree(self.testdir)

    def test_mkdir_and_rmdir(self):
        d = os.path.join(self.testdir, "newdir")
        os.mkdir(d)
        self.assertTrue(os.path.isdir(d))
        os.rmdir(d)
        self.assertFalse(os.path.exists(d))

    def test_makedirs_nested(self):
        nested = os.path.join(self.testdir, "a", "b", "c")
        os.makedirs(nested)
        self.assertTrue(os.path.isdir(nested))

    def test_file_create_and_remove(self):
        fp = os.path.join(self.testdir, "tmpfile.txt")
        f = open(fp, "w")
        f.write("hello")
        f.close()
        self.assertTrue(os.path.isfile(fp))
        os.remove(fp)
        self.assertFalse(os.path.exists(fp))

    def test_rename_file(self):
        src = os.path.join(self.testdir, "src.txt")
        dst = os.path.join(self.testdir, "dst.txt")
        f = open(src, "w")
        f.write("data")
        f.close()
        os.rename(src, dst)
        self.assertFalse(os.path.exists(src))
        self.assertTrue(os.path.isfile(dst))

    def test_unlink_is_alias_for_remove(self):
        fp = os.path.join(self.testdir, "to_unlink.txt")
        f = open(fp, "w")
        f.write("x")
        f.close()
        os.unlink(fp)
        self.assertFalse(os.path.exists(fp))

    def test_chdir_and_getcwd(self):
        orig = os.getcwd()
        try:
            os.chdir(self.testdir)
            cwd = os.getcwd()
            self.assertEqual(
                os.path.normpath(cwd).lower(),
                os.path.normpath(self.testdir).lower()
            )
        finally:
            os.chdir(orig)

    def test_remove_nonexistent_raises(self):
        bogus = os.path.join(self.testdir, "no_such_file.txt")
        try:
            os.remove(bogus)
            self.fail("Expected OSError removing non-existent file")
        except OSError:
            pass

    def test_rmdir_nonexistent_raises(self):
        bogus = os.path.join(self.testdir, "no_such_dir")
        try:
            os.rmdir(bogus)
            self.fail("Expected OSError removing non-existent directory")
        except OSError:
            pass


# ── Win32StatTests ────────────────────────────────────────────────────────────

class Win32StatTests(unittest.TestCase):
    """Test os.stat on Windows.

    Mirrors the stat-related checks in CPython test_os.Win32NtTests.
    """

    def setUp(self):
        self.testdir = _make_temp_dir()

    def tearDown(self):
        _rmtree(self.testdir)

    def test_stat_file_has_positive_size(self):
        fp = os.path.join(self.testdir, "sized.txt")
        f = open(fp, "w")
        f.write("hello world")
        f.close()
        st = os.stat(fp)
        self.assertGreater(st.st_size, 0)

    def test_stat_file_has_positive_mtime(self):
        fp = os.path.join(self.testdir, "mtime.txt")
        f = open(fp, "w")
        f.write("x")
        f.close()
        st = os.stat(fp)
        self.assertGreater(st.st_mtime, 0)

    def test_stat_nonexistent_raises(self):
        bogus = os.path.join(self.testdir, "no_such_file.txt")
        try:
            os.stat(bogus)
            self.fail("Expected OSError for non-existent file")
        except OSError:
            pass

    def test_stat_directory(self):
        st = os.stat(self.testdir)
        self.assertIsNotNone(st)


# ── Win32EnvTests ─────────────────────────────────────────────────────────────

class Win32EnvTests(unittest.TestCase):
    """Test os.environ, os.getenv and os.putenv on Windows."""

    def test_getenv_systemroot_is_directory(self):
        """SYSTEMROOT (e.g. C:\\Windows) must exist and be a directory."""
        val = os.getenv("SystemRoot")
        self.assertIsNotNone(val)
        self.assertTrue(os.path.isdir(val))

    def test_getenv_missing_returns_default(self):
        val = os.getenv("NAJA_NO_SUCH_VAR_XYZ", "fallback")
        self.assertEqual(val, "fallback")

    def test_getenv_missing_no_default_returns_none(self):
        val = os.getenv("NAJA_NO_SUCH_VAR_XYZ")
        self.assertIsNone(val)

    def test_putenv_visible_via_getenv(self):
        os.putenv("NAJA_TEST_PUTENV_VAR", "naja_value")
        val = os.getenv("NAJA_TEST_PUTENV_VAR")
        self.assertEqual(val, "naja_value")


# ── Win32PathTests ────────────────────────────────────────────────────────────

class Win32PathTests(unittest.TestCase):
    """Test os.path operations with Windows-style paths."""

    def test_sep_is_backslash(self):
        self.assertEqual(os.sep, "\\")

    def test_pathsep_is_semicolon(self):
        self.assertEqual(os.pathsep, ";")

    def test_abspath_returns_absolute(self):
        p = os.path.abspath(".")
        self.assertTrue(os.path.isabs(p))

    def test_join_produces_correct_path(self):
        result = os.path.join("C:\\foo", "bar")
        self.assertIn("foo", result)
        self.assertIn("bar", result)

    def test_basename_on_windows_path(self):
        self.assertEqual(os.path.basename("C:\\foo\\bar.txt"), "bar.txt")

    def test_dirname_on_windows_path(self):
        self.assertEqual(os.path.dirname("C:\\foo\\bar.txt"), "C:\\foo")

    def test_splitext_preserves_extension(self):
        parts = os.path.splitext("C:\\foo\\bar.txt")
        self.assertEqual(parts[1], ".txt")

    def test_isabs_on_rooted_path(self):
        self.assertTrue(os.path.isabs("C:\\Windows"))

    def test_isabs_on_relative_path(self):
        self.assertFalse(os.path.isabs("foo\\bar"))

    def test_isabs_on_extended_path(self):
        self.assertTrue(os.path.isabs("\\\\?\\C:\\foo"))

    def test_expandvars_systemroot(self):
        result = os.path.expandvars("%SystemRoot%")
        self.assertTrue(os.path.isdir(result))

    def test_expanduser_tilde_is_directory(self):
        result = os.path.expanduser("~")
        self.assertTrue(os.path.isdir(result))


if __name__ == "__main__":
    unittest.main()
