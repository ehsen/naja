CPython Test Failure Root-Cause Classification
Baseline: 45/63 granular tests pass. 3/3 confirmed-pass tests pass.
---
Category A — Compiler Bugs (not stdlib, not language features — the emitter/resolver is wrong)
These fail even though the Python is legal and the types used are all Naja's own builtins. These are the most important ones because they block tests that don't need any stdlib at all.
Test	Error	Root Cause
test_decorators · test_memoize	'call' != 'double'	call.__name__ = func.__name__ assignment is silently dropped — __name__ setter exists on NajaFunction but attribute store via = on a function object emits to __dict__, not the C# property
test_decorators · test_double	'function' object has no attribute 'abc'	func.__dict__.update(kwds) works, but attribute read on a function (C.foo.abc) doesn't fall through to __dict__
test_decorators · test_single	'C_L74' object has no method 'foo' matching 0 argument(s)	@staticmethod works on C.foo() but breaks when called on an instance C().foo() — descriptor protocol not invoked on instance access
test_decorators · test_staticmethod / test_classmethod	NajaFunction is not NajaFunction	assertIs(wrapper.__func__, func) fails — every __func__ access creates a new NajaFunction wrapper instead of returning the same object
test_decorators · test_dotted	'MiscDecorators' object has no method 'author' matching 1 argument(s)	@MiscDecorators.author("Me") — staticmethod accessed via dotted decorator not dispatching correctly
test_decorators · test_eval_order	'Int64' object has no attribute 'arg'	Class instance is returned as raw Int64 instead of a boxed Python object; attribute access fails
test_decorators · test_argforms	System.Object[] != System.Object[]	args tuple comparison uses reference equality — Object[] equality not structural
test_decorators · test_bound_function_inside_classmethod	type 'B_L299' has no static method 'bar' matching 0	staticmethod descriptor not working when accessed through classmethod body
test_scope.py (full suite)	Undefined name 'fact'	Recursive nested function — fact calls itself, but Naja's closure analysis doesn't treat the function itself as a cell var of its own enclosing scope
test_super.py (full suite)	Undefined name 'E' at f = E.f inside class body	Class body can't resolve module-level names used as values (E.f) — scope chain doesn't reach module scope for class-body attribute assignments
test_compare.py (full suite)	Undefined name 'Cmp' at class body	Same bug — nested class Cmp defined inside a class body, then referenced later in the same class body; class body scope doesn't carry forward
test_baseexception.py (full suite)	Undefined name 'object'	object is only resolved as an implicit base class, not as a value (e.g. assertIsSubclass(Exception, object))
test_isinstance.py (full suite)	Undefined name 'isinstance'	IsInstance(object, object) passed as a first-class callable (not a call site) — not in scope as a value
test_call.py (full suite)	Undefined name 'object'	NULL_OR_EMPTY = object() — object() constructor call, same as above
test_generators.py (full suite)	'yield from' used in non-generator function	yield from inside a function correctly makes it a generator, but the generator-detection pass misses the yield from form — only looks for Yield(object?)
test_yield_from.py (full suite)	Undefined name 'g2'	Nested generator scope resolution bug
test_tuple.py (full suite)	No matching base method 'test_constructors' found on 'Object'	Method override resolution in class hierarchy fails when base method has no direct .NET equivalent
test_property.py (full suite)	IL emission failed: The invoked member is not supported before the type is created	TypeBuilder used before CreateType() — ordering bug in property setter/getter emit
test_fstring.py (full suite)	Unterminated string literal at L1025	F-string parser doesn't handle an edge case in nested/multiline f-strings
test_unary · test_negative	False is not true	isinstance(x, float) returns wrong result — Float subtype check against Int produces false when it should not
test_unary · test_bad_types	Object of type 'String' cannot be converted to Object[]	assertRaises(TypeError, isinstance, I(), C()) — passing IsInstance(object, object) as a callable argument causes wrong call dispatch
test_utf8source · test_pep3120	System.Byte[] != System.Byte[]	bytes == bytes uses reference equality instead of structural
---
Category B — Missing Language Features (not yet implemented in the compiler)
Test	Error	Missing Feature
test_decorators · test_expressions / test_errors	[L1:C6] Expected def or class after decorator	PEP 614: decorators can be arbitrary expressions (@x[0], @a or b) — parser only allows name/dotted/call forms
test_decorators · test_dbcheck	TargetInvocationException	eval() / compile() — dbcheck uses compile(exprstr, ...) and eval() at runtime
test_unary · test_no_overflow	eval() of string expressions is not yet supported	eval() not implemented
test_utf8source · test_latin1	compile() cannot handle Latin-1 source	compile() not implemented
test_utf8source · test_badsyntax	expected exception didn't occur	Source encoding error detection — bad non-UTF-8 source should raise SyntaxError at compile time
test_with.py (full suite)	async/await is not yet supported	async with / async for / async def — entire async subsystem
test_coroutines.py (full suite)	Unexpected token ')'	Coroutine-specific syntax that the parser rejects
---
Category C — Missing Stdlib (the file imports a module Naja.StdLib doesn't have)
These fail at first import — meaning everything else in the file is probably fine.
Test file	First missing import
test_raise.py	import types
test_global.py	import warnings
test_syntax.py	import textwrap
test_list.py	import textwrap
test_metaclass.py	import doctest
test_set.py	import traceback
test_slice.py	import operator
test_math.py	import struct
test_itertools.py	import pickle
test_functools.py	import sys
test_abc.py	import abc
---
Decision Point
You should NOT start stdlib migration yet. Here's why:
1.	Compiler bugs dominate — ~60% of the failures above are in tests that use zero stdlib (test_decorators, test_scope, test_generators, test_property, test_fstring). Fixing stdlib won't help these at all.
2.	Three compiler bugs block the most tests by volume:
•	object/IsInstance(object, object)/builtins-as-values not in scope — affects test_baseexception, test_isinstance, test_call, and any file that passes a builtin as a function argument
•	yield from not detected as generator marker — breaks all yield from files
•	Class-body scope doesn't see module-level names — breaks test_scope, test_super, test_compare
3.	One stdlib module would unblock 4+ files: sys — test_functools, test_bisect, test_os, test_datetime all fail at import sys. But the compiler bugs above would still fail those files at the next line anyway.
Fix the compiler bugs first. Then stdlib migration will show immediate, clean gains.


