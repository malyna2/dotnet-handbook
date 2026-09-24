# Prompt — debrief scorer (for a transcript)

Use this to score a transcript of a mock interview you did with a friend, or of an answer you recorded and transcribed. It is also the prompt to **calibrate first**: score one deliberately weak answer before you trust it with a real one (see Chapter 36, "Calibrate the judge").

```text
You are scoring a candidate's answer from a behavioral interview for a Senior
.NET Backend Engineer. Be strict and literal. Score only what is in the
transcript, never what the candidate probably meant. A 3 must be earned; a 4
is rare.

For each dimension give a score from 1 to 4 and quote the candidate's exact
words as evidence. If there is no evidence for a higher score, give the lower
one.

Structure: 1 = no discernible situation/task/action/result, or the question
was not answered. 2 = STAR present but the situation took over half the
answer. 3 = clear STAR, action is the largest part. 4 = result signposted
early, tight, ends on reflection.
Ownership: 1 = "we" throughout, the candidate's own contribution cannot be
identified. 2 = own part named but thin. 3 = own decisions and actions
explicit. 4 = own decisions explicit AND how they brought other people along.
Specificity: 1 = no numbers or concrete details. 2 = numbers without a
baseline or a method. 3 = before and after, with how it was measured. 4 =
before, after, time window, method, and an honest caveat.
Trade-offs: 1 = one option presented as obvious. 2 = alternatives named but
not weighed. 3 = alternatives weighed against named criteria. 4 = plus what
evidence would have changed the decision.
Reflection: 1 = none, or a humble-brag. 2 = a generic lesson. 3 = a specific
lesson tied to this story. 4 = a specific lesson plus evidence it changed
later behaviour.

Then give: the total out of 20; the single weakest moment in the answer,
quoted; and three concrete edits, each naming the sentence to change. Do not
rewrite the whole answer.

The question: <<<[paste the question]>>>
The transcript: <<<[paste the answer and any follow-ups]>>>
```
