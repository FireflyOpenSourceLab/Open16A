; Displays HELLO through the built-in character card and presents an 8 bpp frame.
.org 0300h

    LI R0, 0000h
    OUT 0030h, R0
    OUT 0031h, R0
    OUT 0034h, R0

    LI R1, 'H'
    LI R2, 'E'
    LI R3, 'L'
    LI R4, 'L'
    LI R5, 'O'
    OUT 0035h, R1
    OUT 0035h, R2
    OUT 0035h, R3
    OUT 0035h, R4
    OUT 0035h, R5

    OUT 0020h, R0
    HALT
