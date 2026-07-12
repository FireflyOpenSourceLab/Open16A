; Open16A BASIC bootstrap interpreter.
; The initial execution core handles direct RUN/CLS plus tokenized PRINT strings and END.

.org 1300h

entry:
    li r0, 0
    out 0037h, r0
    li r0, keyboard_interrupt
    li r1, 0032h
    st.w r0, [r1]
    li r1, 0030h
    st.w r0, [r1]
    ei
    li r1, banner_text
    calla puts
    calla maybe_autorun

ready:
    li r1, ready_text
    calla puts
    li r0, 0
    out 0020h, r0
    li r0, 0
    li r1, input_length
    st.b r0, [r1]

input_loop:
    li r1, 0213h
    ld.bu r2, [r1]
    li r1, 0214h
    ld.bu r3, [r1]
    beq r2, r3, input_wait
    li r4, 0216h
    add r4, r4, r3
    ld.bu r5, [r4]
    li r6, 1
    add r3, r3, r6
    li r6, 001fh
    and r3, r3, r6
    li r4, 0214h
    st.b r3, [r4]

    li r6, 0040h
    and r6, r6, r5
    li r7, 0
    beq r6, r7, input_loop
    li r0, key_lower
    li r6, 0080h
    and r6, r6, r5
    li r7, 0
    beq r6, r7, key_table_ready
    li r0, key_upper
key_table_ready:
    li r6, 003fh
    and r5, r5, r6
    li r6, 002ch
    beq r5, r6, submit_input
    li r6, 000dh
    beq r5, r6, erase_character
    li r6, 000eh
    beq r5, r6, ready

select_key:
    add r0, r0, r5
    ld.bu r0, [r0]
    beq r0, r7, input_loop
    calla append_character
    jmpa input_loop

input_wait:
    li r0, 0
    out 0020h, r0
    halt
    jmpa input_loop

submit_input:
    li r0, 000ah
    calla putc
    calla execute_direct
    jmpa ready

erase_character:
    li r1, input_length
    ld.bu r2, [r1]
    li r3, 0
    beq r2, r3, input_loop
    li r3, 1
    sub r2, r2, r3
    st.b r2, [r1]
    li r0, 0008h
    calla putc
    li r0, 0020h
    calla putc
    li r0, 0008h
    calla putc
    jmpa input_loop

append_character:
    li r1, input_length
    ld.bu r2, [r1]
    li r3, 007fh
    beq r2, r3, append_done
    li r4, input_buffer
    add r4, r4, r2
    st.b r0, [r4]
    li r3, 1
    add r2, r2, r3
    st.b r2, [r1]
    calla putc
append_done:
    ret

execute_direct:
    li r1, input_length
    ld.bu r2, [r1]
    li r3, 3
    bne r2, r3, direct_error
    li r1, input_buffer
    ld.bu r2, [r1]
    li r3, 0072h
    bne r2, r3, direct_cls
    ld.bu r2, [r1 + 1]
    li r3, 0075h
    bne r2, r3, direct_error
    ld.bu r2, [r1 + 2]
    li r3, 006eh
    bne r2, r3, direct_error
    calla run_program
    ret
direct_cls:
    ld.bu r2, [r1]
    li r3, 0063h
    bne r2, r3, direct_error
    ld.bu r2, [r1 + 1]
    li r3, 006ch
    bne r2, r3, direct_error
    ld.bu r2, [r1 + 2]
    li r3, 0073h
    bne r2, r3, direct_error
    li r0, 0
    out 0037h, r0
    out 0020h, r0
    ret
direct_error:
    li r1, syntax_text
    calla puts
    ret

maybe_autorun:
    li r1, 4005h
    ld.bu r0, [r1]
    li r1, 1
    and r0, r0, r1
    li r1, 0
    beq r0, r1, autorun_done
    calla run_program
autorun_done:
    ret

; Interpret the program header at 4000h. Current statement dispatcher supports
; the integer BASIC subset: LET A%=n, PRINT string/int/A%, CLS, REM and END.
run_program:
    li r1, 4000h
    ld.bu r0, [r1]
    li r3, 0042h
    bne r0, r3, no_program
    ld.bu r0, [r1 + 1]
    li r3, 0031h
    bne r0, r3, no_program
    ld.bu r0, [r1 + 2]
    li r3, 0036h
    bne r0, r3, no_program
    ld.bu r0, [r1 + 3]
    li r3, 0050h
    bne r0, r3, no_program
    li r1, 4006h
    ld.w r2, [r1]
    li r1, 400ah
    add r2, r2, r1
run_line:
    beq r1, r2, run_done
    ld.w r3, [r1 + 2]
    li r0, 4
    add r1, r1, r0
    add r4, r1, r3
run_token:
    beq r1, r4, run_line
    ld.bu r0, [r1]
    li r3, 1
    add r1, r1, r3
    li r3, 0091h
    beq r0, r3, run_print
    li r3, 0090h
    beq r0, r3, run_let
    li r3, 0093h
    beq r0, r3, run_if
    li r3, 0095h
    beq r0, r3, run_goto
    li r3, 00b4h
    beq r0, r3, run_poke
    li r3, 00a0h
    beq r0, r3, run_cls
    li r3, 009ch
    beq r0, r3, run_rem
    li r3, 009dh
    beq r0, r3, run_done
    li r3, 009eh
    beq r0, r3, run_done
    li r1, syntax_text
    calla puts
    ret
run_print:
    ld.bu r0, [r1]
    li r3, 1
    add r1, r1, r3
    li r3, 0083h
    beq r0, r3, print_string_begin
    li r3, 0082h
    beq r0, r3, print_integer_literal
    li r3, 0084h
    beq r0, r3, print_integer_variable
    li r3, 00b3h
    beq r0, r3, print_peek
    jmpa run_syntax
print_string_begin:
    ld.bu r5, [r1]
    li r3, 1
    add r1, r1, r3
print_string:
    li r3, 0
    beq r5, r3, print_newline
    ld.bu r0, [r1]
    li r3, 1
    add r1, r1, r3
    calla putc
    li r3, 1
    sub r5, r5, r3
    jmpa print_string
print_newline:
    li r0, 000ah
    calla putc
    jmpa run_token
print_integer_literal:
    ld.w r0, [r1]
    li r3, 2
    add r1, r1, r3
    calla print_integer
    jmpa print_newline
print_integer_variable:
    ld.bu r5, [r1]
    li r3, 1
    add r1, r1, r3
    li r3, 0040h
    and r3, r3, r5
    li r6, 0040h
    bne r3, r6, run_syntax
    li r6, 001fh
    and r5, r5, r6
    add r5, r5, r5
    li r6, integer_vars
    add r6, r6, r5
    ld.w r0, [r6]
    calla print_integer
    jmpa print_newline
print_peek:
    ld.bu r3, [r1]
    li r5, 1
    add r1, r1, r5
    li r5, 0028h
    bne r3, r5, run_syntax
    calla read_integer
    mov r5, r0
    ld.bu r3, [r1]
    li r6, 1
    add r1, r1, r6
    li r6, 0029h
    bne r3, r6, run_syntax
    ld.bu r0, [r5]
    calla print_integer
    jmpa print_newline

run_let:
    ld.bu r0, [r1]
    li r3, 1
    add r1, r1, r3
    li r3, 0084h
    bne r0, r3, run_syntax
    ld.bu r5, [r1]
    li r3, 1
    add r1, r1, r3
    ld.bu r0, [r1]
    li r3, 1
    add r1, r1, r3
    li r3, 003dh
    bne r0, r3, run_syntax
    calla read_integer
    li r3, 0040h
    and r3, r3, r5
    li r6, 0040h
    bne r3, r6, run_syntax
    li r6, 001fh
    and r5, r5, r6
    add r5, r5, r5
    li r6, integer_vars
    add r6, r6, r5
    st.w r0, [r6]
    jmpa run_token

run_goto:
    calla read_integer
    jmpa goto_line

run_poke:
    calla read_integer
    mov r5, r0
    ld.bu r3, [r1]
    li r6, 1
    add r1, r1, r6
    li r6, 002ch
    bne r3, r6, run_syntax
    calla read_integer
    st.b r0, [r5]
    jmpa run_token

run_if:
    calla read_integer
    mov r5, r0
    ld.bu r7, [r1]
    li r3, 1
    add r1, r1, r3
    calla read_integer
    mov r6, r0
    li r3, 003dh
    beq r7, r3, if_equal
    li r3, 003ch
    beq r7, r3, if_less
    li r3, 003eh
    beq r7, r3, if_greater
    jmpa run_syntax
if_equal:
    beq r5, r6, if_true
    jmpa if_false
if_less:
    blt r5, r6, if_true
    jmpa if_false
if_greater:
    bgt r5, r6, if_true
    jmpa if_false
if_true:
    ld.bu r3, [r1]
    li r6, 1
    add r1, r1, r6
    li r6, 0094h
    bne r3, r6, run_syntax
    calla read_integer
    jmpa goto_line
if_false:
    ld.bu r3, [r1]
    li r6, 1
    add r1, r1, r6
    li r6, 0094h
    bne r3, r6, run_syntax
    calla read_integer
    ld.bu r3, [r1]
    li r6, 00b5h
    bne r3, r6, run_token
    li r6, 1
    add r1, r1, r6
    calla read_integer
    jmpa goto_line

; R0 is a target line. R2 remains the program end address.
goto_line:
    mov r5, r0
    li r1, 400ah
goto_line_scan:
    beq r1, r2, run_syntax
    ld.w r3, [r1]
    beq r3, r5, goto_line_found
    ld.w r3, [r1 + 2]
    li r0, 4
    add r1, r1, r0
    add r1, r1, r3
    jmpa goto_line_scan
goto_line_found:
    jmpa run_line

; Reads an INT16 literal or A%-Z% at R1, advances R1, returns it in R0.
; Invalid tokens return zero; statement-level validation handles their context.
read_integer:
    ld.bu r0, [r1]
    li r3, 1
    add r1, r1, r3
    li r3, 0082h
    beq r0, r3, read_integer_literal
    li r3, 002dh
    beq r0, r3, read_integer_negative
    li r3, 0084h
    bne r0, r3, read_integer_invalid
    ld.bu r5, [r1]
    li r3, 1
    add r1, r1, r3
    li r3, 0040h
    and r3, r3, r5
    li r6, 0040h
    bne r3, r6, read_integer_invalid
    li r6, 001fh
    and r5, r5, r6
    add r5, r5, r5
    li r6, integer_vars
    add r6, r6, r5
    ld.w r0, [r6]
    ret
read_integer_literal:
    ld.w r0, [r1]
    li r3, 2
    add r1, r1, r3
    ret
read_integer_negative:
    calla read_integer
    neg r0, r0
    ret
read_integer_invalid:
    li r0, 0
    ret

run_cls:
    li r0, 0
    out 0037h, r0
    jmpa run_token
run_rem:
    mov r1, r4
    jmpa run_line
run_syntax:
    li r1, syntax_text
    calla puts
    ret
run_done:
    li r0, 0
    out 0020h, r0
    ret
no_program:
    li r1, no_program_text
    calla puts
    ret

; Prints signed R0 as decimal while preserving the token cursor in R1/R2/R4.
print_integer:
    push r1
    push r2
    push r3
    push r4
    push r5
    li r1, 0
    bge r0, r1, print_integer_positive
    mov r5, r0
    li r0, 002dh
    calla putc
    neg r0, r5
print_integer_positive:
    li r2, 10
    li r3, 0
print_integer_divide:
    divu r4, r0, r2
    modu r5, r0, r2
    push r5
    li r1, 1
    add r3, r3, r1
    mov r0, r4
    li r1, 0
    bne r0, r1, print_integer_divide
print_integer_emit:
    li r1, 0
    beq r3, r1, print_integer_done
    pop r5
    li r1, 0030h
    add r0, r5, r1
    calla putc
    li r1, 1
    sub r3, r3, r1
    jmpa print_integer_emit
print_integer_done:
    pop r5
    pop r4
    pop r3
    pop r2
    pop r1
    ret

putc:
    out 0035h, r0
    ret

puts:
    ld.bu r0, [r1]
    li r2, 0
    beq r0, r2, puts_done
    li r2, 1
    add r1, r1, r2
    calla putc
    jmpa puts
puts_done:
    ret

keyboard_interrupt:
    iret

banner_text:
    .byte 'O','P','E','N','1','6','A',' ','B','A','S','I','C',' ','0','.','1',10,0
ready_text:
    .byte 'R','E','A','D','Y','.',10,0
syntax_text:
    .byte '?','S','Y','N','T','A','X',' ','E','R','R','O','R',10,0
no_program_text:
    .byte '?','N','O',' ','P','R','O','G','R','A','M',10,0
input_length:
    .byte 0
input_buffer:
    .byte 0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0
integer_vars:
    .word 0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0
    .word 0,0,0,0,0,0,0,0,0,0
    .byte 0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0
    .byte 0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0
    .byte 0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0
    .byte 0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0
    .byte 0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0
    .byte 0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0
    .byte 0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0

; scan 00h-3Eh, unshifted then shifted. Zero denotes a non-text key.
key_lower:
    .byte '`','1','2','3','4','5','6','7','8','9','0','-','=',0,0,0
    .byte 0,'q','w','e','r','t','y','u','i','o','p','[',']',5Ch,0,0
    .byte 0,'a','s','d','f','g','h','j','k','l',';',27h,0,0,'z','x'
    .byte 'c','v','b','n','m',',','.','/',0,0,0,' ',0,0,0
key_upper:
    .byte '~','!','@','#','$','%','^','&','*','(',')','_','+',0,0,0
    .byte 0,'Q','W','E','R','T','Y','U','I','O','P','{','}','|',0,0
    .byte 0,'A','S','D','F','G','H','J','K','L',3Ah,22h,0,0,'Z','X'
    .byte 'C','V','B','N','M','<','>','?',0,0,0,' ',0,0,0
