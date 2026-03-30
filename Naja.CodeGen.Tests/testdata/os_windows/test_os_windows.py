"""Windows-specific os module tests for Naja stdlib.

Mirrors CPython Lib/test/test_os.py Windows test classes but depends only on
modules available in the Naja compiler (os, sys, unittest, subprocess,
_winapi).  No CPython test infrastructure (test.support), no shutil, no
tempfile, no uuid.

All test classes
----------------
Win32ListdirTests   – os.listdir with normal and extended \\\\?\\ paths
Win32ListdriveTests – os.listdrives / os.listvolumes / os.listmounts
Win32SymlinkTests   – os.symlink / os.readlink / os.lstat / os.path.islink
                      (skips automatically when symlink privilege is absent)
Win32JunctionTests  – _winapi.CreateJunction / os.readlink / os.path.islink
Win32FileOpsTests   – mkdir/makedirs/rmdir/remove/unlink/rename/chdir/getcwd
Win32StatTests      – os.stat st_size and st_mtime
Win32EnvTests       – os.environ, os.getenv, os.putenv
Win32PathTests      – os.path operations with Windows-style paths
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


# ── Win32ListdriveTests ───────────────────────────────────────────────────────

class Win32ListdriveTests(unittest.TestCase):
    """Test os.listdrives, os.listvolumes and os.listmounts on Windows.

    Mirrors CPython test_os.Win32ListdriveTests.
    """

    def setUp(self):
        import subprocess
        out = subprocess.check_output(
            ["fsutil.exe", "volume", "list"],
            cwd=os.path.join(os.getenv("SystemRoot", "C:\\Windows"), "System32"),
        )
        lines = out.decode("mbcs", "ignore").splitlines()
        self.known_volumes = [l for l in lines if l.startswith("\\\\?\\")]
        self.known_drives  = [l for l in lines if len(l) >= 3 and l[1:] == ":\\"]
        self.known_mounts  = [l for l in lines if len(l) >= 3 and l[1:3] == ":\\"]

    def test_listdrives(self):
        drives = os.listdrives()
        self.assertIsInstance(drives, list)
        self.assertGreater(len(drives), 0)
        for d in self.known_drives:
            self.assertIn(d, drives)

    def test_listvolumes(self):
        volumes = os.listvolumes()
        self.assertIsInstance(volumes, list)
        self.assertGreater(len(volumes), 0)
        for v in self.known_volumes:
            self.assertIn(v, volumes)

    def test_listmounts(self):
        for volume in os.listvolumes():
            try:
                mounts = os.listmounts(volume)
                self.assertIsInstance(mounts, list)
                for m in mounts:
                    self.assertIn(m, self.known_mounts)
            except OSError:
                pass  # some volumes may be inaccessible


# ── Win32SymlinkTests ─────────────────────────────────────────────────────────

class Win32SymlinkTests(unittest.TestCase):
    """Test os.symlink, os.readlink and os.lstat on Windows.

    Mirrors CPython test_os.Win32SymlinkTests.
    Requires 'Create symbolic links' privilege or Developer Mode.
    """

    def setUp(self):
        self.testdir = _make_temp_dir()
        # Create real targets inside the temp dir so __file__ is not needed.
        self.filelink_target = os.path.join(self.testdir, "target_file.txt")
        f = open(self.filelink_target, "w")
        f.write("symlink target")
        f.close()
        self.dirlink_target = os.path.join(self.testdir, "target_dir")
        os.makedirs(self.dirlink_target)
        self.filelink = os.path.join(self.testdir, "filelinktest")
        self.dirlink  = os.path.join(self.testdir, "dirlinktest")
        # Probe for symlink privilege; skip the whole class if not available.
        probe = os.path.join(self.testdir, "_probe_link")
        try:
            os.symlink(self.dirlink_target, probe)
            os.rmdir(probe)
        except OSError:
            self.skipTest("Cannot create symlinks (requires admin or Developer Mode)")

    def tearDown(self):
        _rmtree(self.testdir)

    def test_directory_link(self):
        os.symlink(self.dirlink_target, self.dirlink)
        self.assertTrue(os.path.exists(self.dirlink))
        self.assertTrue(os.path.isdir(self.dirlink))
        self.assertTrue(os.path.islink(self.dirlink))

    def test_file_link(self):
        os.symlink(self.filelink_target, self.filelink)
        self.assertTrue(os.path.exists(self.filelink))
        self.assertTrue(os.path.isfile(self.filelink))
        self.assertTrue(os.path.islink(self.filelink))

    def test_stat_vs_lstat(self):
        os.symlink(self.filelink_target, self.filelink)
        self.assertEqual(os.stat(self.filelink), os.stat(self.filelink_target))
        self.assertNotEqual(os.lstat(self.filelink), os.stat(self.filelink))

    def test_remove_directory_link_to_missing_target(self):
        target = os.path.join(self.testdir, "missing_target_xyz")
        os.symlink(target, self.dirlink, True)
        os.remove(self.dirlink)
        self.assertFalse(os.path.lexists(self.dirlink))

    def test_isdir_on_directory_link_to_missing_target(self):
        target = os.path.join(self.testdir, "missing_target_xyz")
        os.symlink(target, self.dirlink, True)
        self.assertFalse(os.path.isdir(self.dirlink))

    def test_rmdir_on_directory_link_to_missing_target(self):
        target = os.path.join(self.testdir, "missing_target_xyz")
        os.symlink(target, self.dirlink, True)
        os.rmdir(self.dirlink)

    def test_buffer_overflow(self):
        # Very long paths should raise FileNotFoundError rather than crash.
        segment = "X" * 27
        parts = []
        for i in range(10):
            parts.append(segment)
        path = os.path.join(parts[0], parts[1], parts[2], parts[3], parts[4],
                            parts[5], parts[6], parts[7], parts[8], parts[9])
        test_cases = [
            ("\\" + path, segment),
            (segment, path),
            (path[:180], path[:180]),
        ]
        for src, dest in test_cases:
            try:
                os.symlink(src, dest)
            except OSError:
                pass
            else:
                try:
                    os.remove(dest)
                except OSError:
                    pass

    def test_29248(self):
        all_users = "C:\\Users\\All Users"
        program_data = "C:\\ProgramData"
        if not os.path.lexists(all_users) or not os.path.exists(program_data):
            self.skipTest("Test directories not found")
        target = os.readlink(all_users)
        self.assertTrue(os.path.samefile(target, program_data))


# ── Win32JunctionTests ────────────────────────────────────────────────────────

class Win32JunctionTests(unittest.TestCase):
    """Test NTFS junctions via _winapi.CreateJunction.

    Mirrors CPython test_os.Win32JunctionTests.
    """

    def setUp(self):
        self.testdir = _make_temp_dir()
        self.junction = os.path.join(self.testdir, "junctiontest")
        self.junction_target = self.testdir

    def tearDown(self):
        if os.path.lexists(self.junction):
            os.unlink(self.junction)
        _rmtree(self.testdir)

    def test_create_junction(self):
        import _winapi
        _winapi.CreateJunction(self.junction_target, self.junction)
        self.assertTrue(os.path.lexists(self.junction))
        self.assertTrue(os.path.exists(self.junction))
        self.assertTrue(os.path.isdir(self.junction))
        # bpo-37834: Junctions are NOT treated as symbolic links.
        self.assertFalse(os.path.islink(self.junction))
        # readlink returns a path resolving to the junction target.
        link_path = os.readlink(self.junction)
        if link_path.startswith("\\\\?\\"):
            link_path = link_path[4:]
        self.assertEqual(
            os.path.normcase(os.path.normpath(link_path)),
            os.path.normcase(os.path.normpath(self.junction_target)))

    def test_unlink_removes_junction(self):
        import _winapi
        _winapi.CreateJunction(self.junction_target, self.junction)
        self.assertTrue(os.path.exists(self.junction))
        os.unlink(self.junction)
        self.assertFalse(os.path.exists(self.junction))


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
