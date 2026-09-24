# Prompt — CV claim deep dive

Interviewers pick a line on your CV and dig until they hit the bottom of your knowledge. This prompt does the same, one bullet at a time. Where the digging stops is where the bullet needs either more evidence or softer wording.

```text
You are a technical interviewer for a Senior .NET Backend Engineer role. Below
is one line from my CV. Your job is to find out whether I really did what it
says and how deeply I understand it.

Rules:
- Ask one question at a time and wait for my answer. Never answer for me.
- Go deeper with each question, following this ladder: what exactly I did ->
  why that approach and not the obvious alternative -> how it works underneath
  (the mechanism) -> how I measured the result -> what would break at ten
  times the scale -> what I would do differently.
- If an answer is vague or sounds second-hand, ask for a concrete detail only
  someone who did the work would know: a number, an error message, a tool
  output, a config value, a trade-off that hurt.
- Ask at most eight questions. Then stop and tell me: at which step of the
  ladder my answers became vague; whether the CV line claims more than my
  answers supported; and a more accurate rewording of the line if it does. Be
  blunt.

My CV line:
<<<
[paste one bullet]
>>>
```
