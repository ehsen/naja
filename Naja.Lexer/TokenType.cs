namespace Naja.Lexer;

public enum TokenType
{
    // ── Literals ────────────────────────────────────────────────
    Integer,            // 42  0xFF  0b1010  0o77
    Float,              // 3.14  1e10
    Complex,            // 2j  3.5j  1e2j
    String,             // "hello"  'world'  """multi"""
    FString,            // f"hello {name}"
    Bytes,              // b"raw"
    True,
    False,
    None,

    // ── Identifiers ─────────────────────────────────────────────
    Identifier,

    // ── Keywords ────────────────────────────────────────────────
    And,
    As,
    Assert,
    Async,
    Await,
    Break,
    Class,
    Continue,
    Def,
    Del,
    Elif,
    Else,
    Except,
    Finally,
    For,
    From,
    Global,
    If,
    Import,
    In,
    Is,
    Lambda,
    Match,              // Python 3.10+ soft keyword
    Case,               // Python 3.10+ soft keyword
    Nonlocal,
    Not,
    Or,
    Pass,
    Raise,
    Return,
    Try,
    Type,               // Python 3.12+ soft keyword
    While,
    With,
    Yield,

    // ── Operators ───────────────────────────────────────────────
    Plus,               // +
    Minus,              // -
    Star,               // *
    DoubleStar,         // **
    Slash,              // /
    DoubleSlash,        // //
    Percent,            // %
    At,                 // @  (matrix multiply / decorator)
    Ampersand,          // &
    Pipe,               // |
    Caret,              // ^
    Tilde,              // ~
    LeftShift,          // <<
    RightShift,         // >>

    // ── Augmented Assignment ────────────────────────────────────
    PlusEqual,          // +=
    MinusEqual,         // -=
    StarEqual,          // *=
    DoubleStarEqual,    // **=
    SlashEqual,         // /=
    DoubleSlashEqual,   // //=
    PercentEqual,       // %=
    AtEqual,            // @=
    AmpersandEqual,     // &=
    PipeEqual,          // |=
    CaretEqual,         // ^=
    LeftShiftEqual,     // <<=
    RightShiftEqual,    // >>=

    // ── Comparison ──────────────────────────────────────────────
    Equal,              // ==
    NotEqual,           // !=
    Less,               // <
    LessEqual,          // <=
    Greater,            // >
    GreaterEqual,       // >=

    // ── Assignment ──────────────────────────────────────────────
    Assign,             // =
    Walrus,             // :=   (Python 3.8+)
    Arrow,              // ->   (return type annotation)

    // ── Delimiters ──────────────────────────────────────────────
    LeftParen,          // (
    RightParen,         // )
    LeftBracket,        // [
    RightBracket,       // ]
    LeftBrace,          // {
    RightBrace,         // }
    Comma,              // ,
    Colon,              // :
    Semicolon,          // ;
    Dot,                // .
    Ellipsis,           // ...
    Star2,              // * in parameter lists (different semantic from multiply)

    // ── Structure tokens (Python-specific) ──────────────────────
    Newline,            // logical line ending
    Indent,             // increase in indentation
    Dedent,             // decrease in indentation

    // ── Special ─────────────────────────────────────────────────
    Comment,            // # ...  (usually skipped)
    Eof,
    Unknown,
}
