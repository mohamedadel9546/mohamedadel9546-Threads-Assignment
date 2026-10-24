# Assignment 02 — Answers

Student name:  Mohamed Adel
Group / ID: 1

## What is wrong
: the class contain the race condition and lost update.
the TablesLeft conatin (5) must booked after run is (5) and TablesLeft is (o) but the result error in evrey run 
because muny threads race condition and lost update data. 

## How you fixed it
: use the lock in evrey loop the one thread conatin the object the other thread want to object wait until the thread finish
the result in every run Success.
