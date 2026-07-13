.org 0300h
LI R1, handler
LI R2, 0010h
ST.W R1, [R2]
EI
LI R4, wait
wait:
HALT
JMP R4
handler:
LI R1, FC00h
ST.W R0, [R1 + 2]
LD.BU R2, [R1]
LI R3, 1
ADD R2, R2, R3
ST.B R2, [R1]
IRET
