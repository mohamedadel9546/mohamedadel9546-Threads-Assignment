# Assignment 05 — Answers

Student name:  Mohamed Adel
Group / ID: 1

## Failures you observed
: 
=> The Dictionary must use with one thread  in the (RecordView) method contain two problem race condition lead to Eception.
     because the threads add key more than once.
=> the queue  must use with one thread in PlaceOrder() method the threads overrides and lost data . because not contain the synchronization  

## What you changed
use the ConcurrentDictionary 
use the ConcurrentQueue

## Collection safety vs business-rule safety

-
