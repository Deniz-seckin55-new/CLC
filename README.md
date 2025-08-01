Warning this file is written by AI, I know I'm sorry I'll rewrite it later.

# 🐾 CLC Language Specification: Function Declarations & Denoms

This document defines the syntax and semantics of function declarations and operations (denoms) in the CLC language.

---

## 📛 1. Function Names

- Function names may contain **any characters**.
- **Case-sensitive**.
- Declared using the format:

```clc
(FunctionName ?_ Param1, Param2, ...) (Denom)
```

---

## 🧩 2. Parameters

- Parameters are defined **after** the `?_` separator.
- Multiple parameters are comma-separated:  
  `(sum?_a, b, c)`
- Parameters can be:
  - **Literals** (interpreted as strings)
  - **References to variables** using the `&` prefix

Examples:
```clc
(readln?_input)(&)          // Pass variable 'input'
(println?_Hello World!)(&)  // Pass literal string
```

---

## ⚙️ 3. Denoms (Operation Modifiers)

Denoms determine how the `(FunctionName?_Params)` expression behaves. They are placed in the second set of parentheses and define the execution mode or semantic of the statement.

| Denom Name        | Symbol | Description                                                    | Example |
|-------------------|--------|----------------------------------------------------------------|---------|
| **Set**           | `=`    | Sets a variable to the value or object                         | `(var?_meow)(=)` |
| **Run**           | `&`    | Runs a function with given arguments                           | `(println?_Hello World!)(&)` |
| **Create**        | `*`    | Declares a new variable of the given type                      | `(mystring?_string)(*)` |
| **Type Cast**     | `t`    | Converts the type of a variable **safely**                     | `(x?_int)(t)` |
| **Unsafe Cast**   | `T`    | Force-casts variable type without conversion (dangerous)       | `(x?_int)(T)` |
| **Func Predefine**| `f`    | Declares a function header without implementation              | `(myfunc?_)(f)` |
| **Inline Convert**| `%`    | Converts **literal** values to the specified type immediately   | `(12?_string)(%)` → `12` (int) becomes `"12"` (string) |

---

## 🧠 4. Semantics

- **`?_`**: Separates the function name from its parameters.
- **`&param`**: Interpreted as a variable reference.
- **`param`** (no `&`): Interpreted as a string literal.
- Each denom has a unique purpose and strict evaluation rules.
- **Functions can return values**, but **explicit return types are not yet supported** in the syntax.

---

## 🔐 5. Error Handling

- The language follows **strict error rules**:
  - Invalid denoms
  - Mismatched types
  - Unsafe casts
- Runtime will halt on errors or throw meaningful messages.

---

## ⏳ 6. Future Features

- `async` support planned (likely via `async` keyword).
- Syntax for **explicit return types** will be added.
- More denoms may be introduced (e.g. `!`, `~`) to support concepts like mutability, ownership, etc.

---

## 🧪 7. Examples

```clc
(input?_string)(*)              // Declare 'input' as string
(readln?_input)(&)              // Call 'readln' with variable input
(println?_&input)(&)            // Call 'println' with input's value
(result?_value)(=)              // Assign 'value' to 'result'
(x?_int)(t)                     // Cast x to int safely
(x?_int)(T)                     // Force-cast x to int (unsafe)
(12?_string)(%)                 // Convert literal 12 to string using inline conversion
(myfunc?_)(f)                   // Pre-define a function named 'myfunc'
```

---
