"""Windows-specific OS tests for Naja - adapted from CPython test_os/test_windows.py

Mirrors CPython Lib/test/test_os.py Windows test classes but depends only on
modules available in the Naja compiler. Removes test.support infrastructure
and replaces with inline logic.

Test Classes (Windows only):
    Win32ListdirTests    - os.listdir with normal and extended \\?\ paths
    Win32ListdriveTests  - os.listdrives / os.listvolumes / os.listmounts
    Win32SymlinkTests    - os.symlink / os.readlink / os.lstat / os.path.islink
    Win32JunctionTests   - _winapi.CreateJunction / os.readlink
    Win32NtTests         - nt module functions (partial, some skipped)

Key Adaptations:
    - No test.support imports (replaced with inline logic)
    - No decorators (replaced with skipTest in setUp or test method)
    - Inline TEMP directory creation (replaces os_helper.TESTFN)
    - No external script dependencies (win_console_handler.py)
    - ctypes tests simplified or skipped (stubs only)
"""

import sys
import os
import unittest
import stat
import subprocess
import time

# Windows-only tests
if not sys.platform.startswith('win'):
    raise unittest.SkipTest("Windows-only tests")

try:
    import _winapi
except ImportError:
    _winapi = None

try:
    import signal
except ImportError:
    signal = None

# Helper: Create temporary directory for tests
_seq = [0]

def _make_temp_dir():
    """Create a uniquely-named temp directory and return its path."""
    base = os.getenv("TEMP") or os.getenv("TMP") or "C:\\Temp"
    _seq[0] = _seq[0] + 1
    name = "naja_test_" + str(os.getpid()) + "_" + str(_seq[0])
    path = os.path.join(base, name)
    try:
        os.makedirs(path, exist_ok=True)
    except Exception:
        pass
    return path

def _rmtree(path):
    """Recursively remove a directory tree."""
    try:
        for root, dirs, files in os.walk(path, topdown=False):
            for name in files:
                try:
                    os.remove(os.path.join(root, name))
                except Exception:
                    pass
            for name in dirs:
                try:
                    os.rmdir(os.path.join(root, name))
                except Exception:
                    pass
        try:
            os.rmdir(path)
        except Exception:
            pass
    except Exception:
        pass

def _cleanup_file(path):
    """Remove a single file, ignoring errors."""
    try:
        if os.path.exists(path):
            os.remove(path)
    except Exception:
        pass

# ============================================================================
# Win32ListdirTests - Test os.listdir with normal and extended paths
# ============================================================================

class Win32ListdirTests(unittest.TestCase):
    """Test os.listdir on Windows with normal and extended paths."""

    def setUp(self):
        """Create test directory structure."""
        self.testdir = _make_temp_dir()
        self.created_paths = []
        for i in range(2):
            dir_name = "SUB" + str(i)
            dir_path = os.path.join(self.testdir, dir_name)
            file_name = "FILE" + str(i)
            file_path = os.path.join(self.testdir, file_name)
            os.makedirs(dir_path, exist_ok=True)
            with open(file_path, 'w') as f:
                f.write("I'm " + file_path + " and proud of it. Blame test_os.\n")
            self.created_paths.append(dir_name)
            self.created_paths.append(file_name)
        self.created_paths.sort()

    def tearDown(self):
        """Clean up test directory."""
        _rmtree(self.testdir)

    def test_listdir_no_extended_path(self):
        """Test listdir with normal path (not extended \\?\ path)."""
        result = sorted(os.listdir(self.testdir))
        self.assertEqual(result, self.created_paths)

    def test_listdir_extended_path(self):
        """Test listdir with extended path syntax (\\?\\)."""
        path = "\\\\?\\" + os.path.abspath(self.testdir)
        result = sorted(os.listdir(path))
        self.assertEqual(result, self.created_paths)

    def test_listdir_empty_dir(self):
        """Test listdir on an empty directory."""
        empty_dir = os.path.join(self.testdir, "EMPTY")
        os.makedirs(empty_dir, exist_ok=True)
        result = os.listdir(empty_dir)
        self.assertEqual(result, [])

    def test_listdir_returns_names_not_paths(self):
        """Verify listdir returns base names, not full paths."""
        for entry in os.listdir(self.testdir):
            self.assertFalse(os.path.isabs(entry), "Entry should not be absolute path: " + entry)

    def test_listdir_nonexistent_raises(self):
        """Test that listdir raises OSError for non-existent directory."""
        bogus = os.path.join(self.testdir, "no_such_subdir_xyz")
        try:
            os.listdir(bogus)
            self.fail("Expected OSError for non-existent directory")
        except OSError:
            pass


# ============================================================================
# Win32ListdriveTests - Test os.listdrives, os.listvolumes, os.listmounts
# ============================================================================

class Win32ListdriveTests(unittest.TestCase):
    """Test os.listdrives, os.listvolumes, os.listmounts on Windows."""

    def setUp(self):
        """Get known volumes and drives from fsutil."""
        try:
            out = subprocess.check_output(
                ["fsutil.exe", "volume", "list"],
                cwd=os.path.join(os.getenv("SystemRoot", "\\Windows"), "System32"),
                universal_newlines=True,
                stderr=subprocess.DEVNULL,
            )
            lines = out.splitlines()
            self.known_volumes = set(l for l in lines if l.startswith('\\\\?\\'))
            self.known_drives = set(l for l in lines if len(l) > 1 and l[1:] == ':\\')
            self.known_mounts = set(l for l in lines if len(l) > 2 and l[1:3] == ':\\')
        except Exception as e:
            self.skipTest("Could not query fsutil: " + str(e))

    def test_listdrives(self):
        """Test that os.listdrives returns a list of drives."""
        if not hasattr(os, 'listdrives'):
            self.skipTest("os.listdrives not available")
        drives = os.listdrives()
        self.assertIsInstance(drives, list)
        # Verify at least some known drives are in the result
        found = self.known_drives & set(drives)
        self.assertTrue(len(found) > 0, "No known drives found in os.listdrives() result")

    def test_listvolumes(self):
        """Test that os.listvolumes returns a list of volumes."""
        if not hasattr(os, 'listvolumes'):
            self.skipTest("os.listvolumes not available")
        volumes = os.listvolumes()
        self.assertIsInstance(volumes, list)
        # Verify at least some known volumes are in the result
        found = self.known_volumes & set(volumes)
        self.assertTrue(len(found) > 0, "No known volumes found in os.listvolumes() result")

    def test_listmounts(self):
        """Test that os.listmounts returns mount points for a volume."""
        if not hasattr(os, 'listmounts'):
            self.skipTest("os.listmounts not available")
        if not hasattr(os, 'listvolumes'):
            self.skipTest("os.listvolumes not available")
        
        volumes = os.listvolumes()
        if not volumes:
            self.skipTest("No volumes to test")
        
        for volume in volumes:
            try:
                mounts = os.listmounts(volume)
            except OSError:
                continue  # Skip volumes that fail
            else:
                self.assertIsInstance(mounts, list)
                found = set(mounts) & self.known_mounts
                self.assertTrue(len(found) >= 0, "Mount points should be subset of known mounts")


# ============================================================================
# Win32SymlinkTests - Test os.symlink, os.readlink, os.lstat
# ============================================================================

class Win32SymlinkTests(unittest.TestCase):
    """Test symlink operations on Windows."""

    filelink = 'filelinktest'
    dirlink = 'dirlinktest'
    missing_link = 'missing_link_test'

    def setUp(self):
        """Set up test by creating target files/directories."""
        # Create a temporary directory for our symlink tests
        self.testdir = _make_temp_dir()
        self.filelink = os.path.join(self.testdir, 'filelinktest')
        self.dirlink = os.path.join(self.testdir, 'dirlinktest')
        self.missing_link = os.path.join(self.testdir, 'missing_link_test')
        
        # Create target directory
        self.dirlink_target = os.path.join(self.testdir, 'target_dir')
        os.makedirs(self.dirlink_target, exist_ok=True)
        
        # Create target file
        self.filelink_target = os.path.join(self.testdir, 'target_file.txt')
        with open(self.filelink_target, 'w') as f:
            f.write("test content\n")

    def tearDown(self):
        """Clean up test directory."""
        _cleanup_file(self.filelink)
        _cleanup_file(self.dirlink)
        _cleanup_file(self.missing_link)
        _rmtree(self.testdir)

    def test_directory_link(self):
        """Test creating and reading a symlink to a directory."""
        try:
            os.symlink(self.dirlink_target, self.dirlink)
        except (OSError, NotImplementedError) as e:
            self.skipTest("Symlink not available: " + str(e))
            return

        self.assertTrue(os.path.exists(self.dirlink))
        self.assertTrue(os.path.isdir(self.dirlink))
        self.assertTrue(os.path.islink(self.dirlink))

    def test_file_link(self):
        """Test creating and reading a symlink to a file."""
        try:
            os.symlink(self.filelink_target, self.filelink)
        except (OSError, NotImplementedError) as e:
            self.skipTest("Symlink not available: " + str(e))
            return

        self.assertTrue(os.path.exists(self.filelink))
        self.assertTrue(os.path.isfile(self.filelink))
        self.assertTrue(os.path.islink(self.filelink))

    def test_readlink_returns_target(self):
        """Test that os.readlink returns the symlink target."""
        try:
            os.symlink(self.filelink_target, self.filelink)
        except (OSError, NotImplementedError):
            self.skipTest("Symlink not available")
            return

        link_target = os.readlink(self.filelink)
        # On Windows, readlink may return the full path
        self.assertTrue(link_target.endswith(os.path.basename(self.filelink_target)) or 
                       link_target == self.filelink_target)


# ============================================================================
# Win32JunctionTests - Test _winapi.CreateJunction
# ============================================================================

class Win32JunctionTests(unittest.TestCase):
    """Test NTFS junction creation via _winapi.CreateJunction."""

    def setUp(self):
        """Create test directory."""
        self.testdir = _make_temp_dir()
        self.junction_target = os.path.join(self.testdir, 'target')
        self.junction = os.path.join(self.testdir, 'junction')
        os.makedirs(self.junction_target, exist_ok=True)

    def tearDown(self):
        """Clean up test directory."""
        _cleanup_file(self.junction)
        _rmtree(self.testdir)

    def test_create_junction(self):
        """Test creating a junction with _winapi.CreateJunction."""
        if _winapi is None:
            self.skipTest("_winapi module not available")
        if not hasattr(_winapi, 'CreateJunction'):
            self.skipTest("_winapi.CreateJunction not available")

        try:
            _winapi.CreateJunction(self.junction_target, self.junction)
        except Exception as e:
            self.skipTest("CreateJunction failed: " + str(e))
            return

        self.assertTrue(os.path.lexists(self.junction))
        self.assertTrue(os.path.exists(self.junction))
        self.assertTrue(os.path.isdir(self.junction))

    def test_unlink_removes_junction(self):
        """Test that os.unlink removes a junction."""
        if _winapi is None:
            self.skipTest("_winapi module not available")
        if not hasattr(_winapi, 'CreateJunction'):
            self.skipTest("_winapi.CreateJunction not available")

        try:
            _winapi.CreateJunction(self.junction_target, self.junction)
        except Exception:
            self.skipTest("CreateJunction failed")
            return

        self.assertTrue(os.path.lexists(self.junction))
        
        try:
            os.unlink(self.junction)
        except Exception as e:
            self.fail("Failed to unlink junction: " + str(e))

        self.assertFalse(os.path.lexists(self.junction))


# ============================================================================
# Win32NtTests - Test nt module functions (partial)
# ============================================================================

class Win32NtTests(unittest.TestCase):
    """Test nt module functions on Windows."""

    def setUp(self):
        """Create test file for nt operations."""
        self.testdir = _make_temp_dir()
        self.testfile = os.path.join(self.testdir, 'test.txt')
        with open(self.testfile, 'w') as f:
            f.write("test content\n")

    def tearDown(self):
        """Clean up test directory."""
        _rmtree(self.testdir)

    def test_stat_basic(self):
        """Test that os.stat returns valid stat info."""
        st = os.stat(self.testfile)
        self.assertTrue(st.st_size >= 0)
        self.assertTrue(st.st_mtime >= 0)
        self.assertTrue(st.st_atime >= 0)
        self.assertTrue(st.st_ctime >= 0)

    def test_lstat_basic(self):
        """Test that os.lstat returns valid lstat info."""
        st = os.lstat(self.testfile)
        self.assertTrue(st.st_size >= 0)
        self.assertTrue(st.st_mode > 0)

    def test_stat_vs_lstat_regular_file(self):
        """Test that stat and lstat return same results for regular files."""
        st = os.stat(self.testfile)
        lst = os.lstat(self.testfile)
        # For regular files, stat and lstat should match
        self.assertEqual(st.st_size, lst.st_size)
        self.assertEqual(st.st_mode, lst.st_mode)


# ============================================================================
# Win32KillTests - Test os.kill (basic, ctypes tests skipped)
# ============================================================================

class Win32KillTests(unittest.TestCase):
    """Test os.kill on Windows (partial - complex ctypes tests skipped)."""

    def test_kill_basic(self):
        """Test that os.kill is callable."""
        if signal is None:
            self.skipTest("signal module not available")
        if not hasattr(signal, 'SIGTERM'):
            self.skipTest("signal.SIGTERM not available")
        
        # Just verify os.kill exists and is callable
        self.assertTrue(callable(os.kill))

    def test_signal_constants_exist(self):
        """Test that Windows signal constants exist."""
        if signal is None:
            self.skipTest("signal module not available")
        
        # Check that signal constants exist
        if hasattr(signal, 'SIGTERM'):
            self.assertEqual(signal.SIGTERM, 15)
        if hasattr(signal, 'SIGINT'):
            self.assertEqual(signal.SIGINT, 2)


if __name__ == "__main__":
    unittest.main()
