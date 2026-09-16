# NAJA OVERNIGHT CONFORMANCE SPRINT — OPERATING PLAN
_Auto-maintained by the overnight agent. Last run appends to RUN LOG at bottom._

## MISSION
Make the Naja compiler (github.com/ehsen/naja, Python→.NET IL) pass as many CPython
3.13 tests as possible overnight. Hard priority: CPython conformance FIRST (including
OS-specific tests), .NET interop AFTER. Work autonomously: fix → run related tests →
commit+push → repeat. Nothing may remain uncommitted at the end of a run.

## ENVIRONMENT (VPS, Linux)
- Repo: /home/ubuntu/naja (branch `dev`, remote origin, git author already configured: Naja Bot <naja@local.dev>)
- CPython 3.13 source (READ-ONLY reference, do not modify): /home/ubuntu/dev/cpython
- .NET SDK: /home/ubuntu/.dotnet (10.0.100-rc.2)
- Env exports needed every run:
  export PATH="$HOME/.dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1
  export CPYTHON_REPO="$HOME/dev/cpython" CPYTHON_TEST_ROOT="$HOME/dev/cpython/Lib/test"
- RAM: 7.6GB total, ~2GB available. Do NOT start incus containers (frappe-bench,
  frappe-db, tradingagents are intentionally stopped — NEVER delete or start them).
- Reference docs: /home/ubuntu/naja/Naja.CodeGen/llm_context/*.md
  Triage data: /home/ubuntu/tier_a.json /home/ubuntu/tier_b.json /home/ubuntu/tier_c.json
  (/home/ubuntu/tier_a.json's "Tier A" is optimistic — most of those files need
  test.support infra; trust the empirical FullSuite run over the triage.)

## COMMANDS
- Build all:        dotnet build Naja.slnx -c Release
- Unit suite (must stay 0-failed):
  dotnet test Naja.CodeGen.Tests/Naja.CodeGen.Tests.csproj -c Release --no-build \
    --filter "FullyQualifiedName!~CpythonSuiteRunner"
- CPython gate (18/18 must stay green):
  dotnet test Naja.CodeGen.Tests/Naja.CodeGen.Tests.csproj -c Release --no-build \
    --filter "FullyQualifiedName~CpythonSuiteRunner&FullyQualifiedName!~Bulk"
- CPython FullSuite baseline (~514 files):
  dotnet test Naja.CPythonTests/Naja.CPythonTests.csproj -c Release --no-build \
    --filter "Category=CPythonFullSuite" > /tmp/fullsuite.log 2>&1
  (NOTE: testhost may CRASH mid-run on some file — that aborts the run. When that
  happens, find the last-file(s) in the log and add them to the skip list in
  Naja.CPythonTests/Infrastructure/CPythonTestDiscovery.cs GetAllTestFiles()
  (there is an existing .Where(f => ...) skip chain — extend it), rebuild, rerun.
  Goal: a full un-aborted baseline. Files known to have crashed the host: see RUN LOG.)

## CURRENT STATE (as of 2026-09-16 18:55 UTC)
- dev @ 9ea0275. Unit suite: 537/550 passed, 0 failed, 13 pre-existing skips.
- Phase-1 CPython gate: 18/18 green.
- FullSuite empirical baseline: 12 passed / 186 failed of 198 attempted (host crashed
  at test_ftplib.py area). 316 files never ran (aborted).
- Today's commits: 83c535c (cross-platform + singleton dispatch), 9ec0209 (lexer
  escapes), e0b50e2 (kwargs binding + exception coherence + unittest asserts + float),
  9ea0275 (unified dual-hierarchy exception matcher PythonException.MatchesExpected).

## FAILURE SIGNATURE TABLE (from baseline log /tmp/cpython_fullsuite.log)
Counts of errors across 186 failed files:
- 1554  "has no (static) method 'X'" — top X: subTest(111), _check_in_scopes(156),
        _check_error(87), open(63), dedent(54), get_int_max_str_digits(48),
        TopologicalSorter(42), parse_all(39), assertAddressError(36),
        register_error(33), import_module(30), SequenceMatcher(30), uname(27),
        pack(27), normcase(27)
- 183   NullReferenceException — likely test.support attribute access returning null
- ipaddress module effectively missing (_check_in_scopes/_check_error/assertAddressError)

## PRIORITY QUEUE (work top-down; re-derive from fresh log if stale)
1. [P0] test.support attribute surface: support.TESTFN, support.verbose,
   support.os_helper.*, import_helper.import_module dispatch. Root cause of the 183
   NREs + 30 import_module errors. Check: Naja.StdLib/NajaTestSupport.cs exists and is
   registered as "test.support" in Naja.CodeGen/StdLibResolver.cs + check the second
   stdlib map in Naja.CodeGen/Emitters/Expressions/NameEmitters.cs (~line 458) — both
   maps must know every module. Attr access on NajaTestSupport singleton likely
   returns null for missing props instead of raising — verify GetStaticAttr path.
2. [P0] unittest.subTest (111 uses) — add subTest context manager to NajaUnittest
   (NajaTestCase): no-op pass-through that records failures per-sub-test is enough
   for most tests.
3. [P0] textwrap.dedent "has no static method" (54) — textwrap resolves somewhere it
   shouldn't; verify StdLibResolver + NameEmitters map "textwrap" → Naja.StdLib.NajaTextwrap
   and that the module-call path finds the singleton Instance fallback (json.dumps works,
   textwrap.dedent doesn't — find the difference).
4. [P1] int_max_str_digits API (48): sys.get_int_max_str_digits/set_int_max_str_digits.
5. [P1] graphlib.TopologicalSorter (42) — small pure module, easy C# or Python-embedded impl.
6. [P1] ipaddress module (156+87+36 ≈ 280 errors) — biggest single win; implement
   IPv4Address/IPv6Address/IPv4Network/IPv6Network minimal semantics + assertAddressError
   helper pattern. Effort: large but mechanical.
7. [P1] difflib.SequenceMatcher (30) — implement matching core.
8. [P2] os.uname (27) — Linux: return uname struct via runtime info.
9. [P2] struct.pack/unpack (27) — endian/format parsing minimal set.
10. [P2] urllib.parse.parse_all/parse_qsl family (39+).
11. [P2] email/register_error codecs API (33).
12. Re-run FullSuite after each 2-3 fixes; update this table.

## HARD RULES
1. NEVER delete (or start) the user's incus containers.
2. NEVER leave uncommitted changes at run end — commit+push or revert.
3. Never claim success without tool-verified output; counts must come from real runs.
4. CPython source tree is read-only reference.
5. Each run: exactly ONE fix-cycle (or one crash-resilience fix), then update this
   file + RUN LOG, and end. Keep runs < 25 min.
6. Commit messages: conventional, mention the failing test file(s) fixed.
7. If the gate (18/18) or unit suite (0 failed) regresses, revert the last commit.

## RUN LOG (append one line per run)
- [setup 2026-09-16 18:55 UTC] plan written; baseline recorded (12/198, host crash at test_ftplib).