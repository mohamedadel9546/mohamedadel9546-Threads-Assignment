# Assignment 04 — Answers

Student name:  Mohamed ADEL Moad
Group / ID:

## Why Expected and Actual disagree
-
because [View++] this is not automic operation cause lost update

## What you changed
-
use the interlocked.increment this is automic operaions

## When would you protect the update differently
-
 1- when the interlocked depend on the condition here cause race condition and use [lock] .
 2- the update in interlocked not single varible here become not automic operation. 