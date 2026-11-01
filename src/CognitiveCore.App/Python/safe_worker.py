import ast
import json
import sys

_ALLOWED_BINOPS = {
    ast.Add: lambda a, b: a + b,
    ast.Sub: lambda a, b: a - b,
    ast.Mult: lambda a, b: a * b,
    ast.Div: lambda a, b: a / b,
    ast.FloorDiv: lambda a, b: a // b,
    ast.Mod: lambda a, b: a % b,
    ast.Pow: lambda a, b: a ** b,
}

_ALLOWED_UNARYOPS = {
    ast.UAdd: lambda a: +a,
    ast.USub: lambda a: -a,
}


def _evaluate(node):
    if isinstance(node, ast.Expression):
        return _evaluate(node.body)

    if isinstance(node, ast.Constant):
        if isinstance(node.value, (int, float)) and not isinstance(node.value, bool):
            return node.value
        raise ValueError(f"Constant not allowed: {node.value!r}")

    if isinstance(node, ast.BinOp):
        if type(node.op) not in _ALLOWED_BINOPS:
            raise ValueError(f"Binary op not allowed: {type(node.op).__name__}")
        left = _evaluate(node.left)
        right = _evaluate(node.right)
        return _ALLOWED_BINOPS[type(node.op)](left, right)

    if isinstance(node, ast.UnaryOp):
        if type(node.op) not in _ALLOWED_UNARYOPS:
            raise ValueError(f"Unary op not allowed: {type(node.op).__name__}")
        operand = _evaluate(node.operand)
        return _ALLOWED_UNARYOPS[type(node.op)](operand)

    raise ValueError(f"Node type not allowed: {type(node).__name__}")


def main():
    raw = sys.stdin.read().strip()
    if not raw:
        sys.stdout.write(json.dumps({"success": False, "error": "empty input"}))
        return

    try:
        request = json.loads(raw)
        expression = request.get("expression", "")
        if not expression or not isinstance(expression, str):
            raise ValueError("expression must be a non-empty string")

        parsed = ast.parse(expression, mode="eval")
        result = _evaluate(parsed)
        sys.stdout.write(json.dumps({"success": True, "value": result}))
    except Exception as exc:
        sys.stdout.write(json.dumps({"success": False, "error": str(exc)}))


if __name__ == "__main__":
    main()
