using Xunit;
using Naja.CodeGen;

namespace Naja.CodeGen.Tests.LanguageCompliance;

/// <summary>
/// Exception handling — try/except/else/finally, exception chaining,
/// re-raise, custom exception hierarchies, bare except, and
/// exception semantics that differ subtly from other languages.
/// </summary>
public sealed class ExceptionTests
{
    private static readonly NajaEngine Engine = new();

    private static void Run(string source)
    {
        var dir = Path.Combine(Path.GetTempPath(), "naja_exception_tests");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"exc_{Math.Abs(source.GetHashCode())}.naja");
        File.WriteAllText(path, source.TrimStart());
        Engine.Eval(path);
    }

    private static void RunFile(string fileName) =>
        Engine.Eval(Path.Combine("testdata", "languagecompliance", "exceptions", fileName));

    // ── Full script ───────────────────────────────────────────────────────────

    [Fact]
    public void Exceptions_FullScript() => RunFile("exceptions_full.naja");

    // ── Basic try/except ──────────────────────────────────────────────────────

    [Fact]
    public void TryExcept_CatchesMatchingType()
        => Run("""
            caught = False
            try:
                raise ValueError("test")
            except ValueError:
                caught = True
            assert caught == True
            """);

    [Fact]
    public void TryExcept_DoesNotCatchNonMatchingType()
        => Run("""
            caught_value = False
            caught_other = False
            try:
                try:
                    raise TypeError("type error")
                except ValueError:
                    caught_value = True
            except TypeError:
                caught_other = True
            assert caught_value == False
            assert caught_other == True
            """);

    [Fact]
    public void TryExcept_BindsExceptionToName()
        => Run("""
            try:
                raise ValueError("the message")
            except ValueError as e:
                msg = str(e)
            assert msg == "the message"
            """);

    [Fact]
    public void TryExcept_MultipleClausesFirstMatchWins()
        => Run("""
            def which(exc):
                try:
                    raise exc("x")
                except ValueError:
                    return "value"
                except TypeError:
                    return "type"
                except Exception:
                    return "base"

            assert which(ValueError) == "value"
            assert which(TypeError)  == "type"
            assert which(RuntimeError) == "base"
            """);

    [Fact]
    public void TryExcept_ExceptionSubclassCaughtByBaseClass()
        => Run("""
            class MyError(ValueError): pass

            caught = False
            try:
                raise MyError("sub")
            except ValueError:
                caught = True
            assert caught == True
            """);

    [Fact]
    public void TryExcept_TupleOfExceptionTypes()
        => Run("""
            def catch_multiple(exc_type):
                try:
                    raise exc_type("x")
                except (ValueError, TypeError, KeyError):
                    return "caught"
                except Exception:
                    return "other"

            assert catch_multiple(ValueError) == "caught"
            assert catch_multiple(TypeError)  == "caught"
            assert catch_multiple(KeyError)   == "caught"
            assert catch_multiple(RuntimeError) == "other"
            """);

    // ── try/except/else ───────────────────────────────────────────────────────

    [Fact]
    public void Else_RunsWhenNoException()
        => Run("""
            log = []
            try:
                log.append("try")
            except Exception:
                log.append("except")
            else:
                log.append("else")
            assert log == ["try", "else"]
            """);

    [Fact]
    public void Else_SkippedWhenExceptionRaised()
        => Run("""
            log = []
            try:
                log.append("try")
                raise ValueError()
            except ValueError:
                log.append("except")
            else:
                log.append("else")
            assert log == ["try", "except"]
            """);

    [Fact]
    public void Else_ExceptionInElseIsNotCaughtByExcept()
        => Run("""
            # An exception raised in the else block propagates — it's NOT caught by the except
            caught_outer = False
            try:
                try:
                    pass  # no exception
                except ValueError:
                    pass
                else:
                    raise RuntimeError("from else")
            except RuntimeError:
                caught_outer = True
            assert caught_outer == True
            """);

    // ── try/finally ───────────────────────────────────────────────────────────

    [Fact]
    public void Finally_AlwaysRunsOnSuccess()
        => Run("""
            log = []
            try:
                log.append("try")
            finally:
                log.append("finally")
            assert log == ["try", "finally"]
            """);

    [Fact]
    public void Finally_AlwaysRunsOnException()
        => Run("""
            log = []
            try:
                try:
                    log.append("try")
                    raise ValueError()
                finally:
                    log.append("finally")
            except ValueError:
                log.append("caught")
            assert log == ["try", "finally", "caught"]
            """);

    [Fact]
    public void Finally_AlwaysRunsOnReturn()
        => Run("""
            log = []
            def f():
                try:
                    log.append("try")
                    return "early"
                finally:
                    log.append("finally")

            result = f()
            assert result == "early"
            assert log == ["try", "finally"]
            """);

    [Fact]
    public void Finally_ReturnOverridesExceptionIfItReturns()
        => Run("""
            def f():
                try:
                    raise ValueError("original")
                finally:
                    return "from finally"  # swallows the exception

            result = f()
            assert result == "from finally"
            """);

    // ── Re-raise ──────────────────────────────────────────────────────────────

    [Fact]
    public void Reraise_BareRaise_PreservesOriginal()
        => Run("""
            def inner():
                try:
                    raise ValueError("original")
                except ValueError:
                    raise  # bare re-raise

            try:
                inner()
            except ValueError as e:
                assert str(e) == "original"
            """);

    [Fact]
    public void Reraise_PreservesTraceback()
        => Run("""
            # After re-raise, the exception identity is the same object
            original = None
            reraised = None
            try:
                try:
                    raise ValueError("same object")
                except ValueError as e:
                    original = e
                    raise
            except ValueError as e:
                reraised = e
            assert original is reraised
            """);

    // ── Exception chaining ────────────────────────────────────────────────────

    [Fact]
    public void Chaining_RaiseFrom_SetsCause()
        => Run("""
            try:
                try:
                    raise ValueError("root")
                except ValueError as e:
                    raise RuntimeError("wrapper") from e
            except RuntimeError as e:
                assert str(e.__cause__) == "root"
                assert isinstance(e.__cause__, ValueError)
            """);

    [Fact]
    public void Chaining_ImplicitChain_SetContext()
        => Run("""
            # When exception raised inside except, __context__ is set implicitly
            try:
                try:
                    raise ValueError("first")
                except ValueError:
                    raise RuntimeError("second")  # implicit chain
            except RuntimeError as e:
                assert e.__context__ is not None
                assert isinstance(e.__context__, ValueError)
            """);

    [Fact]
    public void Chaining_RaiseFromNone_SuppressesContext()
        => Run("""
            try:
                try:
                    raise ValueError("original")
                except ValueError:
                    raise RuntimeError("new") from None  # suppress chain
            except RuntimeError as e:
                assert e.__cause__ is None
                assert e.__suppress_context__ == True
            """);

    // ── Custom exception hierarchies ──────────────────────────────────────────

    [Fact]
    public void Custom_SimpleSubclass()
        => Run("""
            class AppError(Exception): pass
            class DatabaseError(AppError): pass
            class ConnectionError(DatabaseError): pass

            try:
                raise ConnectionError("timeout")
            except AppError as e:
                caught_type = type(e).__name__
                caught_msg = str(e)

            assert caught_type == "ConnectionError"
            assert caught_msg == "timeout"
            assert isinstance(ConnectionError("x"), AppError)
            assert isinstance(ConnectionError("x"), Exception)
            """);

    [Fact]
    public void Custom_ExtraAttributes()
        => Run("""
            class HttpError(Exception):
                def __init__(self, status, message):
                    super().__init__(message)
                    self.status = status

            try:
                raise HttpError(404, "Not Found")
            except HttpError as e:
                assert e.status == 404
                assert str(e) == "Not Found"
            """);

    [Fact]
    public void Custom_MultipleInheritance_MRO()
        => Run("""
            class A(Exception): pass
            class B(Exception): pass
            class C(A, B): pass

            # C is caught by both A and B — first match wins
            caught_by = None
            try:
                raise C("test")
            except A:
                caught_by = "A"
            except B:
                caught_by = "B"
            assert caught_by == "A"
            """);

    // ── Bare except ───────────────────────────────────────────────────────────

    [Fact]
    public void BareExcept_CatchesEverything()
        => Run("""
            caught = False
            try:
                raise SystemExit(1)
            except:
                caught = True
            assert caught == True
            """);

    // ── Exception variable scoping ────────────────────────────────────────────

    [Fact]
    public void ExceptionVariable_DeletedAfterExceptBlock()
        => Run("""
            # In Python 3, the 'as e' variable is deleted after the except block
            e_exists_after = False
            try:
                raise ValueError("x")
            except ValueError as e:
                msg = str(e)

            # 'e' should no longer be defined here
            try:
                _ = e  # should raise NameError
                e_exists_after = True
            except NameError:
                e_exists_after = False

            assert e_exists_after == False
            assert msg == "x"
            """);

    // ── Exception in loops ────────────────────────────────────────────────────

    [Fact]
    public void Exception_InsideLoop_ContinuesLoop()
        => Run("""
            results = []
            for i in range(5):
                try:
                    if i == 2:
                        raise ValueError("skip")
                    results.append(i)
                except ValueError:
                    pass

            assert results == [0, 1, 3, 4]
            """);

    [Fact]
    public void Exception_InsideLoop_BreakStillWorks()
        => Run("""
            found = None
            for i in range(10):
                try:
                    if i == 5:
                        raise StopIteration
                    found = i
                except StopIteration:
                    break
            assert found == 4
            """);
}
