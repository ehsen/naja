using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Naja.StdLib.Core;

namespace Naja.StdLib;

/// <summary>
/// Python 'graphlib' module — topologically sort a graph of hashable nodes.
/// Implements TopologicalSorter and CycleError with CPython-identical public
/// API and semantics (ported from /home/ubuntu/dev/cpython/Lib/graphlib.py).
///
/// This single class serves BOTH dispatch paths the Naja compiler uses for a
/// stdlib module whose members are classes:
///   • 'import graphlib' + 'graphlib.TopologicalSorter(g)'  → the singleton
///     Instance's instance method "TopologicalSorter" constructs a sorter
///     (Path A, module-attribute call).
///   • 'from graphlib import TopologicalSorter' + 'TopologicalSorter(g)' →
///     the NajaGraphlib type is resolved and instantiated via CreateDotNet,
///     so its constructor must accept the optional graph (Path B, class call).
/// Each NajaGraphlib instance therefore carries the full sorter state.
/// </summary>
public class NajaGraphlib
{
    public static readonly NajaGraphlib Instance = new();

    // ── CycleError type, exposed for 'graphlib.CycleError' attribute access ──
    public Type CycleError => typeof(PythonCycleError);

    // ── Sorter instance state (faithful port of graphlib.py) ────────────────
    private const int _NODE_OUT = -1;
    private const int _NODE_DONE = -2;

    private readonly Dictionary<object, NodeInfo> _node2info = new();
    private List<object>? _readyNodes;   // null until prepare()
    private long _npassedout;
    private long _nfinished;

    private sealed class NodeInfo
    {
        public object Node;
        public int Npredecessors;
        public readonly List<object> Successors = new();
        public NodeInfo(object node) => Node = node;
    }

    /// <summary>
    /// Constructs a new empty sorter. Called by the Naja compiler when a
    /// 'from graphlib import TopologicalSorter' name is instantiated.
    /// </summary>
    public NajaGraphlib(object? graph = null)
    {
        if (graph is null) return;

        // graph -> { node: predecessors-iterable }. Support both the non-generic
        // IDictionary (System.Collections) and Dictionary<object,object>, and
        // iterate any predecessor collection (list/set/tuple) generically.
        IEnumerable<KeyValuePair<object, object>> pairs = graph switch
        {
            Dictionary<object, object> d => d.Select(kv => new KeyValuePair<object, object>(kv.Key, kv.Value)),
            IDictionary id => id.Cast<DictionaryEntry>().Select(e => new KeyValuePair<object, object>(e.Key, e.Value!)),
            _ => Enumerable.Empty<KeyValuePair<object, object>>()
        };

        foreach (var kv in pairs)
        {
            var preds = CollectPredecessors(kv.Value);
            add(kv.Key, preds.ToArray());
        }
    }

    // Flatten a single predecessor container into a list of predecessor nodes.
    private static List<object> CollectPredecessors(object? value)
    {
        var result = new List<object>();
        if (value is null) return result;
        if (value is IEnumerable e && value is not string && value is not object[])
        {
            foreach (var x in e) result.Add(x!);
        }
        else if (value is object[] arr)
        {
            result.AddRange(arr);
        }
        else
        {
            result.Add(value);
        }
        return result;
    }

    /// <summary>
    /// Module-attribute constructor form: 'graphlib.TopologicalSorter(g)'.
    /// Builds and returns a new sorter seeded with graph.
    /// </summary>
    public object TopologicalSorter(object? graph = null) => new NajaGraphlib(graph);

    private NodeInfo GetNodeInfo(object node)
    {
        if (!_node2info.TryGetValue(node, out var result))
        {
            result = new NodeInfo(node);
            _node2info[node] = result;
        }
        return result;
    }

    public void add(object node, params object[] predecessors)
    {
        if (_readyNodes is not null)
            throw PythonException.ValueError("Nodes cannot be added after a call to prepare()");

        var nodeinfo = GetNodeInfo(node);
        nodeinfo.Npredecessors += predecessors.Length;

        foreach (var pred in predecessors)
        {
            var predInfo = GetNodeInfo(pred);
            predInfo.Successors.Add(node);
        }
    }

    public void prepare()
    {
        if (_readyNodes is not null)
            throw PythonException.ValueError("cannot prepare() more than once");

        _readyNodes = _node2info.Values
            .Where(i => i.Npredecessors == 0)
            .Select(i => i.Node)
            .ToList();

        var cycle = FindCycle();
        if (cycle is not null)
            throw new PythonCycleError("nodes are in a cycle", cycle);
    }

    public object[] get_ready()
    {
        if (_readyNodes is null)
            throw PythonException.ValueError("prepare() must be called first");

        var result = _readyNodes.ToArray();
        var n2i = _node2info;
        foreach (var node in result)
            n2i[node].Npredecessors = _NODE_OUT;

        _readyNodes.Clear();
        _npassedout += result.Length;

        return result;
    }

    public bool is_active()
    {
        if (_readyNodes is null)
            throw PythonException.ValueError("prepare() must be called first");
        return _nfinished < _npassedout || _readyNodes.Count > 0;
    }

    // Python truthiness: 'if ts:' → bool(ts)
    public bool __bool__() => is_active();

    public void done(object node, params object[] more)
    {
        var all = new List<object> { node };
        if (more is not null) all.AddRange(more);
        DoneNodes(all);
    }

    private void DoneNodes(IEnumerable<object> nodes)
    {
        if (_readyNodes is null)
            throw PythonException.ValueError("prepare() must be called first");

        var n2i = _node2info;
        foreach (var node in nodes)
        {
            if (!n2i.TryGetValue(node, out var nodeinfo))
                throw PythonException.ValueError($"node {Repr(node)} was not added using add()");

            var stat = nodeinfo.Npredecessors;
            if (stat != _NODE_OUT)
            {
                if (stat >= 0)
                    throw PythonException.ValueError($"node {Repr(node)} was not passed out (still not ready)");
                if (stat == _NODE_DONE)
                    throw PythonException.ValueError($"node {Repr(node)} was already marked done");
                throw new Exception($"node {Repr(node)}: unknown status {stat}");
            }

            nodeinfo.Npredecessors = _NODE_DONE;

            foreach (var successor in nodeinfo.Successors)
            {
                var successorInfo = n2i[successor];
                successorInfo.Npredecessors -= 1;
                if (successorInfo.Npredecessors == 0)
                    _readyNodes.Add(successor);
            }
            _nfinished += 1;
        }
    }

    private List<object>? FindCycle()
    {
        var n2i = _node2info;
        var stack = new List<object>();
        var itstack = new List<IEnumerator<object>>();
        var seen = new HashSet<object>();
        var node2stacki = new Dictionary<object, int>();

        foreach (var startNode in n2i.Keys)
        {
            if (seen.Contains(startNode)) continue;

            var node = startNode;
            while (true)
            {
                if (seen.Contains(node))
                {
                    if (node2stacki.TryGetValue(node, out var idx))
                        return stack.GetRange(idx, stack.Count - idx).Concat(new[] { node }).ToList();
                }
                else
                {
                    seen.Add(node);
                    itstack.Add(n2i[node].Successors.GetEnumerator());
                    node2stacki[node] = stack.Count;
                    stack.Add(node);
                }

                var advanced = false;
                while (stack.Count > 0)
                {
                    if (itstack[itstack.Count - 1].MoveNext())
                    {
                        node = itstack[itstack.Count - 1].Current;
                        advanced = true;
                        break;
                    }
                    node2stacki.Remove(stack[stack.Count - 1]);
                    stack.RemoveAt(stack.Count - 1);
                    itstack.RemoveAt(itstack.Count - 1);
                }
                if (!advanced || stack.Count == 0) break;
            }
        }
        return null;
    }

    /// <summary>Yields nodes in topological order (CPython static_order generator).</summary>
    public IEnumerable<object> static_order()
    {
        prepare();
        while (is_active())
        {
            var group = get_ready();
            foreach (var n in group) yield return n;
            DoneNodes(group);
        }
    }

    private static string Repr(object? o) => o switch
    {
        null => "None",
        string s => "\"" + s + "\"",
        bool b => b ? "True" : "False",
        long l => l.ToString(),
        _ => Convert.ToString(o) ?? ""
    };
}

/// <summary>
/// graphlib.CycleError — a ValueErrors subclass carrying the detected cycle.
/// e.args == ("nodes are in a cycle", [cycle]); .cycles == [cycle].
/// Subclassing PythonValueError makes it catchable by both 'except CycleError'
/// and 'except ValueError' (Naja dual-hierarchy matcher / Isinst).
/// </summary>
public class PythonCycleError : Core.PythonValueError
{
    public List<object> Cycles { get; }

    public PythonCycleError(string message, List<object> cycle)
        : base(message)
    {
        Cycles = cycle;
    }

    // CPython: e.args == (message, cycle). Returned as a Naja tuple (object[]).
    public object[] args => new object[] { Message, Cycles };
}
