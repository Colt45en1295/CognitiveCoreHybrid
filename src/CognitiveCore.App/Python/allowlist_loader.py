from __future__ import annotations


def build_context(prompt: str, history: list[str]) -> str:
    recent = []
    for item in history[-6:]:
        if item:
            recent.append(str(item).strip())

    history_text = "\n".join(f"- {entry}" for entry in recent) if recent else "- No prior episodes available."

    return (
        "You are CognitiveCore, a hybrid IDE planning and orchestration assistant. "
        "C# owns execution, threading, persistence, cancellation, and HTTP. Python owns "
        "language and reasoning helpers through a strict allowlist.\n\n"
        f"Current user request: {prompt}\n\n"
        "Recent memory:\n"
        f"{history_text}\n\n"
        "Rules:\n"
        "1. Keep the answer practical and implementation-focused.\n"
        "2. Favor deterministic guidance over vague speculation.\n"
        "3. Mention the hybrid architecture clearly when relevant.\n"
        "4. Keep the response concise but useful."
    )


def summarize_prompt(prompt: str) -> str:
    return f"CognitiveCore summary: {prompt.strip()}"
