# Graph-Native Python-to-.NET Compiler in F#

## Document Purpose

This document is written for AI consumption. It is a complete technical specification for implementing a Python-to-.NET compiler using a graph-native architecture in F#. Every design decision is explained with rationale. Every data structure is defined precisely. Every semantic gap between Python and CLR is mapped to a concrete F# bridge strategy. An LLM reading this document should be able to implement any layer of this compiler independently, or extend an existing partial implementation, without requiring additional context.

---

## 1. Core Architectural Insight

### 1.1 The Problem with Traditional Compilers

A traditional compiler is a pipeline of sequential passes, each of which reads mutable shared state, transforms it, and writes back. The components are:

- Lexer → token stream
- Parser → AST
- Symbol table (mutable, shared across passes)
- Scope resolver (mutates symbol table)
- Type inference pass (reads and mutates symbol table)
- CFG builder (reads AST, builds separate structure)
- Optimizer passes (read and mutate CFG)
- Code emitter (reads all of the above)

The bugs that plague this architecture are overwhelmingly **interaction bugs** — pass A leaves state that pass B misreads, or the symbol table is queried before it is fully populated, or a visitor visits nodes in the wrong order. These bugs are found late, via integration tests, and are hard to isolate.

### 1.2 The Graph Insight

Every "algorithm" in a traditional compiler is secretly a graph traversal:

- **Scope resolution** = walk `parent_scope` edges until you find a node that binds the name
- **Type inference** = follow `data_dependency` edges, propagate type attributes, repeat until fixpoint
- **CFG construction** = add `control_flow` edges between statement nodes
- **Cross-module linking** = follow `import` edges between module graphs
- **RBAC (from ERP)** = reachability from role-node to resource-node through `permits` edges

Once you recognise this, the design becomes: **represent the program as a graph from the start, and replace every pass with a graph traversal function**.

### 1.3 The AI-Native Reframe

This compiler is not designed for human readability of its internal representation. The graph is the working memory of the compiler — analogous to RAM. Humans interact with the system in English. The AI reads and reasons over the graph. The only human-facing outputs are:

- English-language intent (input)
- Running .NET assembly (output)
- English-language explanations of graph state (on demand, from AI)

This means the graph schema can be **maximally expressive for AI reasoning** rather than minimally complex for human debugging. Edge kinds can accumulate freely. Node attrs can be verbose. The graph is never shown to a human directly — it is queried by AI and explained in English.

---

## 2. Graph Data Model

### 2.1 Core Types

```fsharp
// Every node in the graph has a unique integer ID
type NodeId = int

// Source location — attached to every node for diagnostics
type SourceSpan = {
    File   : string
    Line   : int
    Col    : int
    EndLine: int
    EndCol : int
}

// The kind of a node — discriminated union, exhaustive pattern matching enforced
type NodeKind =
    // Structural
    | Module
    | FuncDef      of name: string
    | AsyncFuncDef of name: string
    | ClassDef     of name: string
    | Lambda
    // Statements
    | Assign
    | AugAssign    of op: string
    | AnnAssign
    | Return
    | Delete
    | If
    | For
    | AsyncFor
    | While
    | Break
    | Continue
    | Try
    | TryStar                        // Python 3.11 except*
    | With
    | AsyncWith
    | Raise
    | Assert
    | Global       of names: string list
    | Nonlocal     of names: string list
    | Import       of names: (string * string option) list
    | ImportFrom   of modul: string * names: (string * string option) list
    | Pass
    // Expressions
    | BinOp        of op: string
    | UnaryOp      of op: string
    | BoolOp       of op: string
    | Compare      of ops: string list
    | Call
    | IfExp                          // ternary
    | Attribute    of attr: string
    | Subscript
    | Starred
    | NamedExpr                      // walrus :=
    | ListComp
    | SetComp
    | DictComp
    | GeneratorExp
    | Yield
    | YieldFrom
    | Await
    | FormattedValue
    | JoinedStr                      // f-string
    // Leaves
    | Name         of sym: string
    | Const        of value: ConstValue
    | List
    | Tuple
    | Set
    | Dict
    | Slice

type ConstValue =
    | CInt    of int64
    | CBigInt of System.Numerics.BigInteger   // Python unbounded int
    | CFloat  of float
    | CStr    of string
    | CBool   of bool
    | CNone

// Named edge kinds — the semantic relationships between nodes
type EdgeKind =
    // Structural (set during ingest)
    | Child          // ordered AST child
    | Iter           // For/comprehension iterator
    | Target         // assignment target
    | Body           // block body
    | Orelse         // else branch
    | Handler        // except handler
    | Finalbody      // finally block
    | Test           // condition expression
    | Decorator      // decorator chain
    | BaseClass      // class base
    // Semantic (set during analysis)
    | ResolvesTo     // Name/Call → binding site
    | ParentScope    // node → enclosing scope node
    | TypeRef        // node → inferred type node
    | DataDep        // data dependency edge (for worklist)
    | ControlFlow    // CFG edge
    | MROOrder       // ClassDef → ordered base list (C3)
    | CapturedVar    // closure → each variable it captures
    | YieldPoint     // generator FuncDef → each Yield node
    | HandledBy      // Call/construct → F# runtime bridge handler

// Attribute values stored on nodes
type Attr =
    | AString  of string
    | AInt     of int64
    | ABool    of bool
    | ANodeId  of NodeId
    | ANodeIds of NodeId list
    | ASpan    of SourceSpan

// A single node
type Node = {
    Id       : NodeId
    Kind     : NodeKind
    Children : NodeId list                          // ordered structural children
    Edges    : Map<EdgeKind, NodeId list>           // named semantic edges
    Attrs    : Map<string, Attr>                    // analysis-computed attributes
    Span     : SourceSpan
}

// The graph — two separate maps: base facts and derived facts
type Graph = {
    Nodes : Map<NodeId, Node>
    Next  : int
}

// The program — base (from ingest, immutable forever) and analyzed (from passes)
type Program = {
    Base     : Graph    // what the programmer wrote — never mutated after ingest
    Analyzed : Graph    // Base + all edges and attrs added by analysis passes
}
```

### 2.2 Why Two Graphs

`Base` is set once during ingest and never touched again. `Analyzed` is produced by analysis passes — it is a new `Graph` value that contains everything in `Base` plus all the `ResolvesTo`, `TypeRef`, `MROOrder`, `CapturedVar`, `YieldPoint`, and `HandledBy` edges and all the `Attrs` that analysis computed.

This separation means:
- A bug in type inference cannot corrupt scope resolution output — they are separate values
- You can snapshot the graph before any pass and diff it after — instant debugging
- The emitter only ever reads `Analyzed` — it never makes a semantic decision

### 2.3 Graph Operations

```fsharp
module Graph =

    let empty = { Nodes = Map.empty; Next = 0 }

    let addNode (node: Node) (g: Graph) : Graph =
        { g with Nodes = g.Nodes |> Map.add node.Id node
                 Next  = g.Next + 1 }

    let getNode (id: NodeId) (g: Graph) : Node =
        g.Nodes[id]

    let updateNode (id: NodeId) (f: Node -> Node) (g: Graph) : Graph =
        { g with Nodes = g.Nodes |> Map.change id (Option.map f) }

    let addEdge (fromId: NodeId) (kind: EdgeKind) (toId: NodeId) (g: Graph) : Graph =
        g |> updateNode fromId (fun n ->
            let existing = n.Edges |> Map.tryFind kind |> Option.defaultValue []
            { n with Edges = n.Edges |> Map.add kind (existing @ [toId]) })

    let addAttr (nodeId: NodeId) (key: string) (value: Attr) (g: Graph) : Graph =
        g |> updateNode nodeId (fun n ->
            { n with Attrs = n.Attrs |> Map.add key value })

    let followEdge (nodeId: NodeId) (kind: EdgeKind) (g: Graph) : NodeId list =
        g.Nodes[nodeId].Edges |> Map.tryFind kind |> Option.defaultValue []

    let followEdgeOne (nodeId: NodeId) (kind: EdgeKind) (g: Graph) : NodeId option =
        g |> followEdge nodeId kind |> List.tryHead

    let children (nodeId: NodeId) (g: Graph) : NodeId list =
        g.Nodes[nodeId].Children

    let attr (nodeId: NodeId) (key: string) (g: Graph) : Attr option =
        g.Nodes[nodeId].Attrs |> Map.tryFind key
```

---

## 3. Ingest — Python Source to Graph

### 3.1 Parser Strategy

Do not use CPython's `ast` module. It requires a Python runtime dependency. Instead, implement a PEG parser directly in F# using **FParsec** (single NuGet dependency). The grammar source is `Grammar/python.gram` in the CPython repository — it is a PEG grammar that FParsec can implement directly.

The parser emits `NodeId` values directly into a mutable `Graph` as it parses. There is no intermediate AST. The graph is built in a single left-to-right pass over the source.

```fsharp
// Parser state — mutable graph being built, immutable after ingest
type IngestState = {
    mutable Graph : Graph
    Source        : string
    File          : string
}

// Every parser combinator returns NodeId — building the graph as it parses
let parseConst (state: IngestState) : Parser<NodeId, unit> =
    choice [
        pint64    |>> (fun n -> ingestNode state (Const (CInt n)) [])
        pfloat    |>> (fun f -> ingestNode state (Const (CFloat f)) [])
        stringLit |>> (fun s -> ingestNode state (Const (CStr s)) [])
        keyword "True"  >>% ingestNode state (Const (CBool true))  []
        keyword "False" >>% ingestNode state (Const (CBool false)) []
        keyword "None"  >>% ingestNode state (Const CNone) []
    ]

let ingestNode (state: IngestState) (kind: NodeKind) (children: NodeId list) : NodeId =
    let id   = state.Graph.Next
    let node = { Id = id; Kind = kind; Children = children
                 Edges = Map.empty; Attrs = Map.empty
                 Span  = currentSpan state }
    state.Graph <- state.Graph |> Graph.addNode node
    id
```

### 3.2 Key Ingest Rules

- Every Python statement and expression becomes exactly one node.
- Children are ordered — the order of `Children` in a node reflects source order.
- No semantic edges are set during ingest. `ResolvesTo`, `TypeRef`, `MROOrder` etc. are all set by analysis passes.
- `SourceSpan` is recorded on every node. This is non-negotiable — without it, diagnostic messages cannot point to source locations.
- Decorators are ingested as nodes and attached via `Decorator` edges to their `FuncDef` or `ClassDef`.

### 3.3 Scope Seed During Ingest

During ingest, set `ParentScope` edges structurally — that is, every node gets a `ParentScope` edge pointing to the nearest enclosing `FuncDef`, `ClassDef`, `Lambda`, or `Module` node. This is purely structural and can be set during ingest without any semantic analysis.

```fsharp
// Scope stack maintained during ingest
type ScopeStack = NodeId list

let pushScope (id: NodeId) (stack: ScopeStack) : ScopeStack = id :: stack
let currentScope (stack: ScopeStack) : NodeId = List.head stack

// When ingesting any node, record its parent scope
let ingestNodeInScope (state: IngestState) (kind: NodeKind) (children: NodeId list)
                      (scope: ScopeStack) : NodeId =
    let id = ingestNode state kind children
    let parentScopeId = currentScope scope
    state.Graph <- state.Graph |> Graph.addEdge id ParentScope parentScopeId
    id
```

---

## 4. Analysis Passes

All analysis passes have the same signature: `Graph -> Graph`. They take the current graph and return a new graph with additional edges and attrs. They never mutate. The F# type system enforces this.

### 4.1 Pass Pipeline

```fsharp
let analyze (base: Graph) : Graph =
    base
    |> resolveScopes        // adds ResolvesTo edges on Name and Call nodes
    |> inferTypes           // adds TypeRef attrs on expression nodes
    |> analyzeClosures      // adds CapturedVar edges on FuncDef nodes
    |> analyzeMRO           // adds MROOrder edges on ClassDef nodes
    |> analyzeGenerators    // adds YieldPoint edges, sets is_generator attr
    |> analyzeAsync         // sets is_async attr, marks suspension points
    |> assignBridges        // adds HandledBy edges for CLR semantic gaps
    |> assignLocals         // assigns msil_local_idx attrs to Name bindings
```

### 4.2 Scope Resolution

```fsharp
let resolveScopes (g: Graph) : Graph =
    g.Nodes |> Map.fold (fun acc id node ->
        match node.Kind with
        | Name sym ->
            match resolveNameInScope g id sym with
            | Some bindingId ->
                acc |> Graph.addEdge id ResolvesTo bindingId
            | None ->
                // Unresolved name — builtin or error
                acc |> Graph.addAttr id "unresolved" (ABool true)
        | _ -> acc
    ) g

// Walk parent scope chain until a binding is found
let rec resolveNameInScope (g: Graph) (fromId: NodeId) (name: string) : NodeId option =
    let node = g |> Graph.getNode fromId
    // Check if this scope node binds the name
    match findBinding g fromId name with
    | Some id -> Some id
    | None    ->
        // Walk up to parent scope
        match g |> Graph.followEdgeOne fromId ParentScope with
        | Some parentId -> resolveNameInScope g parentId name
        | None          -> None   // reached Module with no binding — unresolved

// A scope node "binds" a name if it has a FuncDef, Assign, For target,
// Import, or ClassDef child whose name matches
let findBinding (g: Graph) (scopeId: NodeId) (name: string) : NodeId option =
    let scope = g |> Graph.getNode scopeId
    scope.Children |> List.tryPick (fun childId ->
        let child = g |> Graph.getNode childId
        match child.Kind with
        | FuncDef n      when n = name -> Some childId
        | ClassDef n     when n = name -> Some childId
        | Assign                       -> findAssignTarget g childId name
        | For                          -> findForTarget    g childId name
        | Import names                 -> names |> List.tryPick (fun (n, alias) ->
            if Option.defaultValue n alias = name then Some childId else None)
        | _                            -> None)
```

### 4.3 Fixpoint Type Inference — Worklist Algorithm

Do not run type inference as a full-graph pass repeatedly. Use a worklist: maintain a queue of nodes whose inputs changed and only reprocess those nodes.

```fsharp
let inferTypes (g: Graph) : Graph =
    // Seed worklist with all leaf nodes (Const, Name with resolved binding)
    let initialQueue =
        g.Nodes |> Map.toSeq
        |> Seq.filter (fun (_, n) -> isLeaf n)
        |> Seq.map fst
        |> Queue

    let rec loop (current: Graph) (queue: Queue<NodeId>) =
        if queue.Count = 0 then current
        else
            let id    = queue.Dequeue()
            let node  = current |> Graph.getNode id
            let inferred = inferNodeType current node
            match current |> Graph.attr id "inferred_type" with
            | Some existing when existing = AString inferred ->
                loop current queue   // no change — don't requeue dependents
            | _ ->
                let updated = current |> Graph.addAttr id "inferred_type" (AString inferred)
                // Requeue all nodes that have a DataDep edge from this node
                current |> Graph.followEdge id DataDep
                |> List.iter queue.Enqueue
                loop updated queue

    loop g initialQueue

let inferNodeType (g: Graph) (node: Node) : string =
    match node.Kind with
    | Const (CInt _)    -> "int"
    | Const (CFloat _)  -> "float"
    | Const (CStr _)    -> "str"
    | Const (CBool _)   -> "bool"
    | Const CNone       -> "NoneType"
    | Const (CBigInt _) -> "bigint"
    | Name _ ->
        match g |> Graph.followEdgeOne node.Id ResolvesTo with
        | Some bindingId ->
            g |> Graph.attr bindingId "inferred_type"
            |> Option.map (function AString s -> s | _ -> "unknown")
            |> Option.defaultValue "unknown"
        | None -> "unknown"
    | BinOp op ->
        let leftType  = g |> Graph.attr node.Children[0] "inferred_type"
        let rightType = g |> Graph.attr node.Children[1] "inferred_type"
        inferBinOpType op leftType rightType
    | _ -> "unknown"
```

### 4.4 Closure Analysis

```fsharp
let analyzeClosures (g: Graph) : Graph =
    g.Nodes |> Map.fold (fun acc id node ->
        match node.Kind with
        | FuncDef _ | Lambda | AsyncFuncDef _ ->
            let captured = findCapturedVars g id
            captured |> List.fold (fun a capturedId ->
                a |> Graph.addEdge id CapturedVar capturedId) acc
        | _ -> acc
    ) g

// A variable is captured if it is referenced inside a FuncDef
// but bound in an enclosing scope (not the FuncDef itself)
let findCapturedVars (g: Graph) (funcId: NodeId) : NodeId list =
    let funcScope = funcId
    allDescendantNames g funcId
    |> List.choose (fun nameId ->
        match g |> Graph.followEdgeOne nameId ResolvesTo with
        | Some bindingId ->
            let bindingScope = g |> Graph.followEdgeOne bindingId ParentScope
            if bindingScope <> Some funcScope then Some bindingId
            else None
        | None -> None)
    |> List.distinct
```

### 4.5 MRO — C3 Linearisation

```fsharp
let analyzeMRO (g: Graph) : Graph =
    g.Nodes |> Map.fold (fun acc id node ->
        match node.Kind with
        | ClassDef _ ->
            let bases = g |> Graph.followEdge id BaseClass
            let mro   = c3Linearise g bases
            mro |> List.fold (fun a baseId ->
                a |> Graph.addEdge id MROOrder baseId) acc
        | _ -> acc
    ) g

// Standard C3 linearisation algorithm — pure function over graph
let rec c3Linearise (g: Graph) (bases: NodeId list) : NodeId list =
    match bases with
    | []  -> []
    | [b] ->
        let parentBases = g |> Graph.followEdge b BaseClass
        b :: c3Linearise g parentBases
    | _   ->
        let seqs = bases |> List.map (fun b ->
            b :: (g |> Graph.followEdge b BaseClass))
        merge g seqs

let merge (g: Graph) (seqs: NodeId list list) : NodeId list =
    match seqs |> List.filter (not << List.isEmpty) with
    | []   -> []
    | seqs ->
        let candidate =
            seqs |> List.tryPick (fun seq ->
                let head = List.head seq
                let inTail = seqs |> List.exists (fun s ->
                    List.tail s |> List.contains head)
                if not inTail then Some head else None)
        match candidate with
        | None   -> failwith "Cannot create consistent MRO — circular inheritance"
        | Some c ->
            let rest = seqs
                       |> List.map (List.filter ((<>) c))
                       |> List.filter (not << List.isEmpty)
            c :: merge g rest
```

### 4.6 Generator Analysis

```fsharp
let analyzeGenerators (g: Graph) : Graph =
    g.Nodes |> Map.fold (fun acc id node ->
        match node.Kind with
        | FuncDef _ | AsyncFuncDef _ ->
            let yields = findDescendantYields g id
            if List.isEmpty yields then acc
            else
                let withFlag   = acc |> Graph.addAttr id "is_generator" (ABool true)
                yields |> List.fold (fun a yId ->
                    a |> Graph.addEdge id YieldPoint yId) withFlag
        | _ -> acc
    ) g

let findDescendantYields (g: Graph) (funcId: NodeId) : NodeId list =
    // Walk all descendants of funcId — collect Yield and YieldFrom nodes
    // Do not descend into nested FuncDef nodes (they are separate generators)
    let rec walk id =
        let node = g |> Graph.getNode id
        match node.Kind with
        | FuncDef _ | AsyncFuncDef _ when id <> funcId -> []  // stop at nested func
        | Yield | YieldFrom -> [id]
        | _ -> node.Children |> List.collect walk
    walk funcId
```

### 4.7 Bridge Assignment

This pass annotates every node that requires a CLR semantic bridge. The emitter reads these attrs and routes to the correct F# handler. No decision-making happens in the emitter.

```fsharp
let assignBridges (g: Graph) : Graph =
    g.Nodes |> Map.fold (fun acc id node ->
        match node.Kind with
        | FuncDef _ | AsyncFuncDef _ ->
            let isGen   = g |> Graph.attr id "is_generator" = Some (ABool true)
            let isAsync = node.Kind = AsyncFuncDef ""
            let hasCap  = g |> Graph.followEdge id CapturedVar |> List.isEmpty |> not
            let bridge  =
                if isGen && isAsync then "async_generator"
                elif isGen          then "state_machine"
                elif isAsync        then "async_method"
                elif hasCap         then "display_class"
                else                     "plain_method"
            acc |> Graph.addAttr id "bridge" (AString bridge)

        | Call ->
            let dispatch =
                match g |> Graph.followEdgeOne id ResolvesTo with
                | None          -> "dlr"          // unresolved — dynamic dispatch
                | Some targetId ->
                    match (g |> Graph.getNode targetId).Kind with
                    | FuncDef _      -> "static"
                    | AsyncFuncDef _ -> "static_async"
                    | Name _         -> "delegate"  // first-class function
                    | _              -> "dlr"
            acc |> Graph.addAttr id "dispatch" (AString dispatch)

        | ClassDef _ ->
            let bases = g |> Graph.followEdge id MROOrder
            let needsMixin = bases.Length > 1
            acc |> Graph.addAttr id "needs_mixin" (ABool needsMixin)

        | Tuple when isAssignTarget g id ->
            acc |> Graph.addAttr id "bridge" (AString "tuple_unpack")

        | _ -> acc
    ) g
```

### 4.8 Local Variable Assignment

This pass assigns `msil_local_idx` attrs to every local variable binding, so the emitter knows which `ldloc`/`stloc` index to use.

```fsharp
let assignLocals (g: Graph) : Graph =
    // For each FuncDef, collect all Name nodes in the scope that are
    // local bindings (not args, not globals, not free vars)
    g.Nodes |> Map.fold (fun acc id node ->
        match node.Kind with
        | FuncDef _ | AsyncFuncDef _ ->
            let locals = collectLocals g id
            locals |> List.mapi (fun i localId -> (localId, i))
            |> List.fold (fun a (localId, idx) ->
                a |> Graph.addAttr localId "msil_local_idx" (AInt (int64 idx))) acc
        | _ -> acc
    ) g
```

---

## 5. MSIL Emission

### 5.1 Emitter State

```fsharp
open System.Reflection
open System.Reflection.Emit

type EmitState = {
    Assembly  : AssemblyBuilder
    Module    : ModuleBuilder
    Graph     : Graph                          // the fully analyzed graph
    Methods   : Map<NodeId, MethodBuilder>     // FuncDef NodeId → MethodBuilder
    Types     : Map<NodeId, TypeBuilder>       // ClassDef NodeId → TypeBuilder
    Labels    : Map<NodeId, Label>             // For/While → loop labels
}
```

### 5.2 Entry Point

```fsharp
let emitProgram (program: Program) : AssemblyBuilder =
    let asmName  = AssemblyName("CompiledPython")
    let asm      = AssemblyBuilder.DefineDynamicAssembly(asmName, AssemblyBuilderAccess.Run)
    let modul    = asm.DefineDynamicModule("Main")

    let state = { Assembly = asm; Module = modul
                  Graph = program.Analyzed
                  Methods = Map.empty; Types = Map.empty; Labels = Map.empty }

    // Two-pass emit: first declare all types and methods (so forward references work)
    let state' = declareAll state
    // Then emit bodies
    emitBodies state'
    asm

// Pass 1: declare TypeBuilder and MethodBuilder for every ClassDef and FuncDef
let declareAll (state: EmitState) : EmitState =
    state.Graph.Nodes |> Map.fold (fun s id node ->
        match node.Kind with
        | ClassDef name ->
            let tb = s.Module.DefineType(name, TypeAttributes.Public)
            { s with Types = s.Types |> Map.add id tb }
        | FuncDef name | AsyncFuncDef name ->
            let retType = typeof<obj>    // default — refined by type inference attr
            let tb      = s.Module.DefineType("__module__", TypeAttributes.Public)
            let mb      = tb.DefineMethod(name, MethodAttributes.Public ||| MethodAttributes.Static,
                                          retType, [| typeof<obj> |])
            { s with Methods = s.Methods |> Map.add id mb }
        | _ -> s
    ) state
```

### 5.3 Node Emission — Core Dispatch

```fsharp
let rec emitNode (state: EmitState) (il: ILGenerator) (nodeId: NodeId) : unit =
    let g    = state.Graph
    let node = g |> Graph.getNode nodeId

    match node.Kind with

    | Const cv ->
        match cv with
        | CInt n    -> il.Emit(OpCodes.Ldc_I4, int n)
        | CFloat f  -> il.Emit(OpCodes.Ldc_R8, f)
        | CStr s    -> il.Emit(OpCodes.Ldstr, s)
        | CBool b   -> il.Emit(if b then OpCodes.Ldc_I4_1 else OpCodes.Ldc_I4_0)
        | CNone     -> il.Emit(OpCodes.Ldnull)
        | CBigInt n -> emitBigInt il n

    | Name _ ->
        // ResolvesTo edge tells us exactly where this name is bound
        // storage attr tells us what opcode to use — no decision here
        match g |> Graph.followEdgeOne nodeId ResolvesTo with
        | None -> il.Emit(OpCodes.Ldnull)  // unresolved — emit null
        | Some bindingId ->
            match g |> Graph.attr bindingId "storage" with
            | Some (AString "local") ->
                let idx = g |> Graph.attr bindingId "msil_local_idx"
                            |> Option.map (function AInt i -> int i | _ -> 0)
                            |> Option.defaultValue 0
                il.Emit(OpCodes.Ldloc, idx)
            | Some (AString "arg") ->
                let idx = g |> Graph.attr bindingId "arg_idx"
                            |> Option.map (function AInt i -> int i | _ -> 0)
                            |> Option.defaultValue 0
                il.Emit(OpCodes.Ldarg, idx)
            | Some (AString "field") ->
                let fi = resolveField state bindingId
                il.Emit(OpCodes.Ldfld, fi)
            | _ ->
                il.Emit(OpCodes.Ldnull)

    | BinOp op ->
        emitNode state il node.Children[0]
        emitNode state il node.Children[1]
        match op with
        | "Add" -> il.Emit(OpCodes.Add)
        | "Sub" -> il.Emit(OpCodes.Sub)
        | "Mul" -> il.Emit(OpCodes.Mul)
        | "Div" -> il.Emit(OpCodes.Div)
        | "Mod" -> il.Emit(OpCodes.Rem)
        | "Pow" -> il.Emit(OpCodes.Call, typeof<System.Math>.GetMethod("Pow"))
        | "BitAnd" -> il.Emit(OpCodes.And)
        | "BitOr"  -> il.Emit(OpCodes.Or)
        | "BitXor" -> il.Emit(OpCodes.Xor)
        | "LShift" -> il.Emit(OpCodes.Shl)
        | "RShift" -> il.Emit(OpCodes.Shr)
        | _        -> emitDynamicBinOp il op

    | Compare ops ->
        emitNode state il node.Children[0]
        ops |> List.iteri (fun i op ->
            emitNode state il node.Children[i + 1]
            match op with
            | "Eq"    -> il.Emit(OpCodes.Ceq)
            | "NotEq" -> il.Emit(OpCodes.Ceq); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ceq)
            | "Lt"    -> il.Emit(OpCodes.Clt)
            | "Gt"    -> il.Emit(OpCodes.Cgt)
            | "LtE"   -> il.Emit(OpCodes.Cgt); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ceq)
            | "GtE"   -> il.Emit(OpCodes.Clt); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ceq)
            | "Is"    -> il.Emit(OpCodes.Ceq)
            | "In"    -> il.Emit(OpCodes.Call, runtimeContains)
            | _       -> ())

    | If ->
        let testId   = g |> Graph.followEdgeOne nodeId Test   |> Option.get
        let bodyId   = g |> Graph.followEdgeOne nodeId Body   |> Option.get
        let elseId   = g |> Graph.followEdgeOne nodeId Orelse
        let elseLabel = il.DefineLabel()
        let endLabel  = il.DefineLabel()
        emitNode state il testId
        il.Emit(OpCodes.Brfalse, elseLabel)
        emitNode state il bodyId
        il.Emit(OpCodes.Br, endLabel)
        il.MarkLabel(elseLabel)
        elseId |> Option.iter (emitNode state il)
        il.MarkLabel(endLabel)

    | For ->
        let iterId   = g |> Graph.followEdgeOne nodeId Iter   |> Option.get
        let targetId = g |> Graph.followEdgeOne nodeId Target |> Option.get
        let bodyId   = g |> Graph.followEdgeOne nodeId Body   |> Option.get
        let loopStart = il.DefineLabel()
        let loopEnd   = il.DefineLabel()
        emitNode state il iterId
        il.Emit(OpCodes.Call, runtimeGetEnumerator)
        il.MarkLabel(loopStart)
        il.Emit(OpCodes.Dup)
        il.Emit(OpCodes.Callvirt, moveNextMethod)
        il.Emit(OpCodes.Brfalse, loopEnd)
        il.Emit(OpCodes.Dup)
        il.Emit(OpCodes.Callvirt, getCurrentMethod)
        emitStore state il targetId
        emitNode state il bodyId
        il.Emit(OpCodes.Br, loopStart)
        il.MarkLabel(loopEnd)
        il.Emit(OpCodes.Pop)   // pop the enumerator

    | While ->
        let testId  = g |> Graph.followEdgeOne nodeId Test |> Option.get
        let bodyId  = g |> Graph.followEdgeOne nodeId Body |> Option.get
        let loopStart = il.DefineLabel()
        let loopEnd   = il.DefineLabel()
        il.MarkLabel(loopStart)
        emitNode state il testId
        il.Emit(OpCodes.Brfalse, loopEnd)
        emitNode state il bodyId
        il.Emit(OpCodes.Br, loopStart)
        il.MarkLabel(loopEnd)

    | Return ->
        node.Children |> List.iter (emitNode state il)
        il.Emit(OpCodes.Ret)

    | Assign ->
        let targetId = g |> Graph.followEdgeOne nodeId Target |> Option.get
        let valueId  = node.Children |> List.last
        emitNode state il valueId
        emitStore state il targetId

    | FuncDef _ | AsyncFuncDef _ ->
        // Route to bridge handler — decision was made in assignBridges pass
        match g |> Graph.attr nodeId "bridge" with
        | Some (AString "state_machine")   -> emitStateMachine    state nodeId
        | Some (AString "display_class")   -> emitDisplayClass    state nodeId
        | Some (AString "async_method")    -> emitAsyncMethod     state nodeId
        | Some (AString "async_generator") -> emitAsyncGenerator  state nodeId
        | _                                -> emitPlainMethod     state nodeId

    | Call ->
        node.Children |> List.iter (emitNode state il)
        match g |> Graph.attr nodeId "dispatch" with
        | Some (AString "static") ->
            let targetId = g |> Graph.followEdgeOne nodeId ResolvesTo |> Option.get
            let mb       = state.Methods[targetId]
            il.Emit(OpCodes.Call, mb)
        | Some (AString "delegate") ->
            il.Emit(OpCodes.Callvirt, invokeDelegateMethod)
        | _ ->
            emitDynamicCall il nodeId state

    | Try ->
        let bodyId    = g |> Graph.followEdgeOne nodeId Body      |> Option.get
        let handlers  = g |> Graph.followEdge    nodeId Handler
        let finallyId = g |> Graph.followEdgeOne nodeId Finalbody
        let elseId    = g |> Graph.followEdgeOne nodeId Orelse
        emitTryBlock state il bodyId handlers finallyId elseId

    | ClassDef _ ->
        emitClassDef state nodeId

    | Pass -> ()   // no-op — emit nothing

    | Import _ | ImportFrom _ ->
        emitImport state il nodeId

    | Yield ->
        // Only reachable from within emitStateMachine — not from here
        // The state machine handler breaks the function into parts at yield points
        failwith "Yield node emitted outside state machine context"

    | _ ->
        // Unimplemented node kind — emit a runtime error call
        il.Emit(OpCodes.Ldstr, sprintf "Unimplemented: %A" node.Kind)
        il.Emit(OpCodes.Call, typeof<System.Console>.GetMethod("WriteLine", [|typeof<string>|]))
```

---

## 6. F# Bridge Handlers — Python Semantics with No CLR Equivalent

### 6.1 Generator State Machine

Python generators (`yield`) have no CLR primitive. They compile to a state machine class implementing `IEnumerator<obj>`. This is the same transformation C# `yield return` undergoes.

```fsharp
let emitStateMachine (state: EmitState) (funcId: NodeId) : unit =
    let g        = state.Graph
    let funcNode = g |> Graph.getNode funcId
    let funcName = match funcNode.Kind with FuncDef n -> n | _ -> "__gen"
    let yields   = g |> Graph.followEdge funcId YieldPoint

    // Define the state machine class
    let tb = state.Module.DefineType(
                 funcName + "__StateMachine",
                 TypeAttributes.Private ||| TypeAttributes.Sealed)
    tb.AddInterfaceImplementation(typeof<System.Collections.Generic.IEnumerator<obj>>)

    // Fields: __state (int), __current (obj), one field per local that crosses a yield
    let stateFld   = tb.DefineField("__state",   typeof<int>, FieldAttributes.Private)
    let currentFld = tb.DefineField("__current", typeof<obj>, FieldAttributes.Private)

    // Capture locals that are live across yield points
    let capturedLocals = g |> Graph.followEdge funcId CapturedVar
    let capturedFields =
        capturedLocals |> List.map (fun localId ->
            let name = g |> Graph.attr localId "sym" |> Option.map (function AString s -> s | _ -> "_") |> Option.defaultValue "_"
            tb.DefineField(name, typeof<obj>, FieldAttributes.Public))

    // MoveNext: switch on __state, jump to correct yield resume point
    let moveNext = tb.DefineMethod("MoveNext",
                                   MethodAttributes.Public ||| MethodAttributes.Virtual,
                                   typeof<bool>, [||])
    let il = moveNext.GetILGenerator()

    // Emit switch: load __state, switch to labels
    let labels = yields |> List.map (fun _ -> il.DefineLabel())
    let doneLabel = il.DefineLabel()
    il.Emit(OpCodes.Ldarg_0)
    il.Emit(OpCodes.Ldfld, stateFld)
    il.Emit(OpCodes.Switch, labels |> Array.ofList)
    il.Emit(OpCodes.Br, doneLabel)

    // Emit each segment between yield points
    yields |> List.iteri (fun i yieldId ->
        il.MarkLabel(labels[i])
        // Emit code up to this yield point
        emitSegment state il funcId yieldId i stateFld currentFld
        il.Emit(OpCodes.Ldc_I4_1)   // return true (has more values)
        il.Emit(OpCodes.Ret))

    il.MarkLabel(doneLabel)
    il.Emit(OpCodes.Ldc_I4_0)    // return false (exhausted)
    il.Emit(OpCodes.Ret)

    // get_Current property
    let getCurrent = tb.DefineMethod("get_Current",
                                     MethodAttributes.Public ||| MethodAttributes.Virtual,
                                     typeof<obj>, [||])
    let ilc = getCurrent.GetILGenerator()
    ilc.Emit(OpCodes.Ldarg_0)
    ilc.Emit(OpCodes.Ldfld, currentFld)
    ilc.Emit(OpCodes.Ret)

    tb.CreateType() |> ignore
```

### 6.2 Closure Display Class

Python closures capture variables by reference. CLR closures are implemented as display classes — a generated class that holds the captured variables as fields.

```fsharp
let emitDisplayClass (state: EmitState) (funcId: NodeId) : unit =
    let g        = state.Graph
    let funcNode = g |> Graph.getNode funcId
    let funcName = match funcNode.Kind with FuncDef n -> n | _ -> "__closure"
    let captured = g |> Graph.followEdge funcId CapturedVar

    // Define the display class
    let tb = state.Module.DefineType(
                 funcName + "__DisplayClass",
                 TypeAttributes.Private ||| TypeAttributes.Sealed)

    // One field per captured variable
    let capturedFields =
        captured |> List.map (fun varId ->
            let name = g |> Graph.attr varId "sym"
                         |> Option.map (function AString s -> s | _ -> "_")
                         |> Option.defaultValue "_"
            tb.DefineField(name, typeof<obj>, FieldAttributes.Public))

    // Define the method inside the display class
    let mb = tb.DefineMethod(funcName,
                             MethodAttributes.Public,
                             typeof<obj>,
                             [| typeof<obj> |])
    let il = mb.GetILGenerator()
    // Emit the function body — Name loads now go through fields, not ldloc
    emitFuncBody { state with Types = state.Types |> Map.add funcId tb } il funcId
    il.Emit(OpCodes.Ret)
    tb.CreateType() |> ignore
```

### 6.3 Multiple Inheritance — MRO and Interface Mixins

CLR supports one base class and multiple interfaces. Python's multiple inheritance is mapped by:
- First class in MRO order = CLR base class
- Remaining classes = CLR interfaces with explicit method implementations

```fsharp
let emitClassDef (state: EmitState) (classId: NodeId) : unit =
    let g         = state.Graph
    let classNode = g |> Graph.getNode classId
    let className = match classNode.Kind with ClassDef n -> n | _ -> "__class"
    let mro       = g |> Graph.followEdge classId MROOrder

    let tb = state.Types[classId]

    match mro with
    | []            -> ()   // no bases — inherits from object implicitly
    | primary :: mixins ->
        // First in MRO = real CLR base class
        let baseType = state.Types |> Map.tryFind primary
                       |> Option.map (fun t -> t :> System.Type)
                       |> Option.defaultValue typeof<obj>
        // Note: TypeBuilder parent must be set at definition time
        // This is why declareAll runs first

        // Remaining = interfaces
        mixins |> List.iter (fun mixinId ->
            match state.Types |> Map.tryFind mixinId with
            | Some mixinType ->
                tb.AddInterfaceImplementation(mixinType)
                // Emit explicit interface method implementations
                emitMixinMethods state tb mixinId
            | None -> ())

    // Emit class body
    let bodyId = g |> Graph.followEdgeOne classId Body |> Option.get
    emitNode state (tb.DefineMethod("__init__",
                                    MethodAttributes.Public,
                                    typeof<System.Void>, [||])
                   |> fun mb -> mb.GetILGenerator()) bodyId

    tb.CreateType() |> ignore
```

### 6.4 Multiple Return Values — ValueTuple

Python functions returning multiple values use implicit tuple packing. CLR maps this to `ValueTuple`.

```fsharp
let emitMultiReturn (state: EmitState) (il: ILGenerator) (tupleId: NodeId) : unit =
    let g     = state.Graph
    let items = (g |> Graph.getNode tupleId).Children
    // Push all items onto stack
    items |> List.iter (emitNode state il)
    // Create ValueTuple of appropriate arity
    let tupleType =
        match items.Length with
        | 2 -> typeof<System.ValueTuple<obj, obj>>
        | 3 -> typeof<System.ValueTuple<obj, obj, obj>>
        | 4 -> typeof<System.ValueTuple<obj, obj, obj, obj>>
        | n -> failwithf "Tuple arity %d not directly supported — use nested tuples" n
    let ctor = tupleType.GetConstructor(Array.create items.Length typeof<obj>)
    il.Emit(OpCodes.Newobj, ctor)

// Tuple unpack on assignment: a, b = fn()
let emitTupleUnpack (state: EmitState) (il: ILGenerator) (targetId: NodeId) (valueId: NodeId) : unit =
    let g       = state.Graph
    let targets = (g |> Graph.getNode targetId).Children
    // Emit value expression — pushes a ValueTuple onto stack
    emitNode state il valueId
    // Store in a temp local, then load each field
    let tmpLocal = il.DeclareLocal(typeof<obj>)
    il.Emit(OpCodes.Stloc, tmpLocal)
    targets |> List.iteri (fun i tId ->
        il.Emit(OpCodes.Ldloc, tmpLocal)
        let fieldName = sprintf "Item%d" (i + 1)
        let fi = tmpLocal.LocalType.GetField(fieldName)
        il.Emit(OpCodes.Ldfld, fi)
        emitStore state il tId)
```

### 6.5 Arbitrary Precision Integer

Python integers are unbounded. CLR `int64` silently overflows. The type inference pass detects when an integer literal exceeds `int64` range or when `**` (power) is used on integers, and marks the node with `bridge = "bigint"`.

```fsharp
let emitBigInt (il: ILGenerator) (value: System.Numerics.BigInteger) : unit =
    // Serialize BigInteger to byte array, emit as literal
    let bytes = value.ToByteArray()
    il.Emit(OpCodes.Ldc_I4, bytes.Length)
    il.Emit(OpCodes.Newarr, typeof<byte>)
    bytes |> Array.iteri (fun i b ->
        il.Emit(OpCodes.Dup)
        il.Emit(OpCodes.Ldc_I4, i)
        il.Emit(OpCodes.Ldc_I4, int b)
        il.Emit(OpCodes.Stelem_I1))
    let bigIntCtor = typeof<System.Numerics.BigInteger>
                         .GetConstructor([| typeof<byte[]> |])
    il.Emit(OpCodes.Newobj, bigIntCtor)
```

### 6.6 Exception Groups — Python 3.11+

`except*` syntax groups multiple exceptions. CLR maps this to `AggregateException` with filter handlers.

```fsharp
let emitTryStar (state: EmitState) (il: ILGenerator) (nodeId: NodeId) : unit =
    let g        = state.Graph
    let bodyId   = g |> Graph.followEdgeOne nodeId Body    |> Option.get
    let handlers = g |> Graph.followEdge    nodeId Handler
    let endLabel = il.DefineLabel()

    il.BeginExceptionBlock() |> ignore
    emitNode state il bodyId

    // Each handler becomes a catch AggregateException with a filter
    handlers |> List.iter (fun handlerId ->
        let exType = resolveExceptionType state g handlerId
        il.BeginCatchBlock(typeof<System.AggregateException>)
        // Filter: check if any inner exception matches exType
        il.Emit(OpCodes.Call, aggregateExceptionFilter exType)
        let skipLabel = il.DefineLabel()
        il.Emit(OpCodes.Brfalse, skipLabel)
        emitNode state il handlerId
        il.MarkLabel(skipLabel))

    il.EndExceptionBlock()
    il.MarkLabel(endLabel)
```

---

## 7. Full Compilation Pipeline

```fsharp
let compile (source: string) (fileName: string) : AssemblyBuilder =
    // Step 1: Parse source into base graph
    let baseGraph = parse source fileName

    // Step 2: Run analysis passes — each is Graph → Graph
    let analyzedGraph =
        baseGraph
        |> resolveScopes
        |> inferTypes         // worklist-based fixpoint
        |> analyzeClosures
        |> analyzeMRO
        |> analyzeGenerators
        |> analyzeAsync
        |> assignBridges
        |> assignLocals

    let program = { Base = baseGraph; Analyzed = analyzedGraph }

    // Step 3: Emit .NET assembly
    emitProgram program

// Parallel compilation for large codebases
let compileAll (sources: (string * string) list) : AssemblyBuilder =
    // Each module compiles independently — immutable graphs make this free
    let moduleGraphs =
        sources
        |> List.map (fun (src, file) -> async { return parse src file })
        |> Async.Parallel
        |> Async.RunSynchronously

    // Merge all module graphs into one
    let merged = moduleGraphs |> Array.fold mergeGraphs Graph.empty

    // Analyze the merged graph — cross-module ResolvesTo edges resolved here
    let analyzed =
        merged
        |> resolveScopes
        |> inferTypes
        |> analyzeClosures
        |> analyzeMRO
        |> analyzeGenerators
        |> analyzeAsync
        |> assignBridges
        |> assignLocals

    emitProgram { Base = merged; Analyzed = analyzed }
```

---

## 8. Python-to-CLR Semantic Gap Reference

This table maps every Python construct that has no direct CLR equivalent to its F# bridge strategy. The graph's `bridge` attr on each node determines which handler is invoked. The emitter never makes this decision — it reads the attr.

| Python construct | CLR gap | Bridge strategy | Graph attr |
|---|---|---|---|
| `yield` / generator | No coroutine primitive | State machine class, `IEnumerator<obj>` | `bridge = "state_machine"` |
| `async def` | Task-based async | `Task<obj>` + `async` state machine | `bridge = "async_method"` |
| `async def` + `yield` | Async generator | `IAsyncEnumerable<obj>` state machine | `bridge = "async_generator"` |
| Closure over mutable var | Needs ref capture | Display class with captured fields | `bridge = "display_class"` |
| Multiple inheritance | One base class only | Primary base + interface mixins | `needs_mixin = true` + `MROOrder` edges |
| `a, b = fn()` | One return value | `ValueTuple<obj,obj>` | `bridge = "tuple_unpack"` |
| Duck-typed call | Type required for `callvirt` | DLR `CallSite<Func<...>>` | `dispatch = "dlr"` |
| First-class function | Arity mismatch | `Func<obj[],obj>` uniform delegate | `dispatch = "delegate"` |
| `int` (unbounded) | `int64` overflows | `System.Numerics.BigInteger` | `bridge = "bigint"` |
| `except*` | One exception per catch | `AggregateException` + filter | `bridge = "exception_group"` |
| `with` statement | No `using` in MSIL | `try/finally` + `IDisposable` call | emitted inline |
| `**kwargs` | No named variadic | `Dictionary<string,obj>` | `bridge = "kwargs"` |
| `*args` | Arity variadic | `obj[]` params | `bridge = "varargs"` |
| `@decorator` | No annotation execution | Wrapper method call at definition | emitted inline |
| List/set/dict comprehension | No native comprehension | Inline `For` loop with collection init | emitted inline |
| f-string | No interpolation opcode | `String.Format` or concat chain | emitted inline |
| `__dunder__` methods | CLR operator overloading | Map to CLR operator methods | resolved at `ClassDef` emit |

---

## 9. Testing Strategy

### 9.1 Three-Tier Test Architecture

Because the graph separates base facts from derived facts, tests can be written at three independent levels:

**Tier 1 — Graph shape tests (ingest only)**
```fsharp
[<Fact>]
let ``for loop produces correct graph structure`` () =
    let g = parse "for i in range(10): print(i)" "test.py"
    let forNode = g.Nodes |> Map.values |> Seq.find (fun n -> n.Kind = For)
    Assert.True(g |> Graph.followEdgeOne forNode.Id Iter   |> Option.isSome)
    Assert.True(g |> Graph.followEdgeOne forNode.Id Target |> Option.isSome)
    Assert.True(g |> Graph.followEdgeOne forNode.Id Body   |> Option.isSome)
```

**Tier 2 — Analysis correctness tests**
```fsharp
[<Fact>]
let ``name inside for loop resolves to loop target`` () =
    let src = "for i in range(10): print(i)"
    let analyzed = parse src "test.py" |> resolveScopes
    let printCall = findNode analyzed (fun n -> n.Kind = Call)
    let argName   = (analyzed |> Graph.getNode printCall).Children |> List.head
    let resolved  = analyzed |> Graph.followEdgeOne argName ResolvesTo
    Assert.True(resolved.IsSome)
    let binding   = analyzed |> Graph.getNode resolved.Value
    Assert.Equal(For, binding.Kind |> kindBase)
```

**Tier 3 — Emit correctness tests (CPython suite)**
```fsharp
[<Fact>]
let ``compiled for loop produces same output as CPython`` () =
    let src = "for i in range(1, 10): print(i)"
    let asm = compile src "test.py"
    let output = runAssembly asm
    let expected = runCPython src
    Assert.Equal(expected, output)
```

### 9.2 Diagnosing CPython Suite Failures

When a tier 3 test fails, the diagnosis path is:

1. Dump the analyzed graph for the failing input
2. Inspect `ResolvesTo` edges — are all names resolved correctly? (tier 1/2 bug)
3. Inspect `bridge` and `dispatch` attrs — are bridge strategies assigned correctly? (tier 2 bug)
4. If graph looks correct, isolate the handler and test with a minimal synthetic graph (tier 3 bug)

```fsharp
// Graph dump for AI-readable diagnostics
let dumpGraph (g: Graph) : string =
    g.Nodes |> Map.toSeq
    |> Seq.map (fun (id, node) ->
        let edges = node.Edges |> Map.toSeq
                    |> Seq.map (fun (k, vs) -> sprintf "%A→[%s]" k (vs |> List.map string |> String.concat ","))
                    |> String.concat " "
        let attrs = node.Attrs |> Map.toSeq
                    |> Seq.map (fun (k, v) -> sprintf "%s=%A" k v)
                    |> String.concat " "
        sprintf "N%d %A children=[%s] %s %s"
            id node.Kind
            (node.Children |> List.map string |> String.concat ",")
            edges attrs)
    |> String.concat "\n"
```

---

## 10. Project Structure

```
PythonCompiler/
├── PythonCompiler.fsproj
├── Core/
│   ├── Types.fs          -- NodeId, NodeKind, EdgeKind, Attr, Node, Graph, Program
│   ├── GraphOps.fs       -- addNode, addEdge, addAttr, followEdge, updateNode
│   └── SourceSpan.fs     -- SourceSpan type and helpers
├── Parser/
│   ├── Lexer.fs          -- FParsec token parsers
│   ├── Expressions.fs    -- expression parsers → NodeId
│   ├── Statements.fs     -- statement parsers → NodeId
│   └── Ingest.fs         -- top-level parse entry point
├── Analysis/
│   ├── ScopeResolver.fs  -- resolveScopes
│   ├── TypeInference.fs  -- inferTypes (worklist)
│   ├── ClosureAnalysis.fs -- analyzeClosures
│   ├── MROAnalysis.fs    -- analyzeMRO + c3Linearise
│   ├── GeneratorAnalysis.fs -- analyzeGenerators
│   ├── AsyncAnalysis.fs  -- analyzeAsync
│   ├── BridgeAssignment.fs -- assignBridges
│   └── LocalAssignment.fs -- assignLocals
├── Emit/
│   ├── EmitState.fs      -- EmitState type, declareAll
│   ├── CoreEmitter.fs    -- emitNode dispatch
│   ├── Bridges/
│   │   ├── StateMachine.fs   -- generator bridge
│   │   ├── DisplayClass.fs   -- closure bridge
│   │   ├── AsyncBridge.fs    -- async method bridge
│   │   ├── MixinEmit.fs      -- multiple inheritance bridge
│   │   ├── TupleUnpack.fs    -- multiple return bridge
│   │   ├── DynamicDispatch.fs -- DLR callsite bridge
│   │   └── BigIntBridge.fs   -- arbitrary precision bridge
│   └── Pipeline.fs       -- compile, compileAll entry points
├── Diagnostics/
│   └── GraphDump.fs      -- dumpGraph for AI diagnostics
└── Tests/
    ├── GraphShapeTests.fs    -- tier 1
    ├── AnalysisTests.fs      -- tier 2
    └── CPythonSuiteTests.fs  -- tier 3
```

---

## 11. Dependencies

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <LangVersion>preview</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <!-- Only external dependency: PEG parser combinators -->
    <PackageReference Include="FParsec" Version="1.1.1" />
  </ItemGroup>
</Project>
```

Everything else — `System.Reflection.Emit`, `System.Numerics.BigInteger`, `System.Collections.Generic`, `System.Runtime.CompilerServices` — is in the .NET BCL and requires no additional packages.

---

## 12. Key Invariants — What Must Always Be True

An AI extending or debugging this compiler should verify these invariants when anything is unexpected:

1. **Base graph is never mutated after ingest.** All analysis produces new `Graph` values.
2. **Every `Name` node in `Analyzed` has a `ResolvesTo` edge or an `unresolved = true` attr.** No silent missing resolutions.
3. **Every `FuncDef` and `AsyncFuncDef` node has a `bridge` attr.** The emitter must never decide bridge strategy itself.
4. **Every `Call` node has a `dispatch` attr.** Static, delegate, or DLR — always explicit.
5. **Every node has a valid `SourceSpan`.** No span-less nodes.
6. **`MROOrder` edges are in C3 linearisation order.** First edge = primary CLR base. Remaining = mixins.
7. **`YieldPoint` edges exist on every `FuncDef` with `is_generator = true`.** The state machine handler depends on them.
8. **`CapturedVar` edges exist on every `FuncDef` with `bridge = "display_class"`.** The display class handler depends on them.
9. **`msil_local_idx` attrs are assigned to every local binding node.** Emitter uses these directly — no re-computation.
10. **`Children` list is ordered.** Order matches Python source order. Emitters depend on positional indexing (`node.Children[0]` = left operand of `BinOp`, etc.).
