# Naja 🦀
### *Python syntax. .NET iron. No compromises.*

Naja is a compiler that takes **Python 3.14+ source code** and compiles it directly to **.NET 10 IL** via `System.Reflection.Emit` — no C# intermediate, no interpretation, no CIR shim.

Write Python. Get a real .NET assembly. Call WinForms. Call WPF. Reference any NuGet package.

```python
# hello_winforms.py  — this is the goal
import clr
from System.Windows.Forms import Form, Button, Application
from System.Drawing import Size, Point

class MainForm(Form):
    def __init__(self):
        self.Text = "Built with Naja"
        self.Size = Size(400, 300)
        btn = Button()
        btn.Text = "Hello from Python IL"
        btn.Click += lambda s, e: print("clicked")
        self.Controls.Add(btn)

Application.Run(MainForm())
```

```bash
ferrous compile hello_winforms.py --target net10-windows --out hello.exe
./hello.exe   # real .NET executable, no runtime bridge
```

---

## Architecture

```
Python 3.14+ Source
        │
        ▼
   [Naja.Lexer]          Token stream with INDENT/DEDENT
        │
        ▼
   [Naja.Parser]         AST (immutable C# records)
        │
        ▼
   [Naja.Semantics]      Symbol tables, type inference
        │
        ▼
   [Naja.CodeGen]        Direct IL via System.Reflection.Emit
        │
        ▼
   .NET 10 Assembly (.exe / .dll)
```

## Build Status

| Phase | Status | Notes |
|-------|--------|-------|
| Lexer | ✅ Complete | Full Python 3.14 token set, INDENT/DEDENT |
| Parser | 🔄 In progress | Recursive descent |
| Semantics | ⬜ Planned | |
| IL Emitter | ⬜ Planned | The payoff |
| Runtime Lib | ⬜ Planned | `print()`, `range()`, `list`, `dict` |

## Getting Started

```bash
git clone https://github.com/yourname/ferrous
cd ferrous
dotnet build
dotnet test
```

## Why?

Because IronPython is stuck at Python 3.4. Because CSnakes is just interop.  
Because nobody has done **real Python → IL compilation** targeting .NET 10.  
Until now.

---

*Naja is a research/portfolio compiler project. Not production ready.*
