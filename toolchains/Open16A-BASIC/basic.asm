; Open16A BASIC bootstrap interpreter.
; The initial execution core handles direct RUN/CLS plus tokenized PRINT strings and END.

.org 1300h

entry:
    ; Runtime buffers live in the gap between string variables and arrays.
    ; Only numeric variables require explicit reset; buffer lengths own data.
    li r1, 7440h
    li r2, 104
    li r0, 0
entry_clear_numeric_variables:
    st.b r0, [r1]
    li r3, 1
    add r1, r1, r3
    sub r2, r2, r3
    li r3, 0
    bne r2, r3, entry_clear_numeric_variables
    li r0, 0
    out 0037h, r0
    li r1, break_requested
    st.b r0, [r1]
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
    calla present_current_mode
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
    calla present_current_mode
    halt
    jmpa input_loop

submit_input:
    calla newline
    li r1, input_length
    ld.bu r2, [r1]
    li r1, 7340h
    add r1, r1, r2
    li r0, 0
    st.b r0, [r1]
    li r1, input_mode
    ld.bu r3, [r1]
    li r1, 1
    beq r3, r1, submit_runtime_input
    calla execute_direct
    jmpa ready
submit_runtime_input:
    calla parse_runtime_input
    li r1, input_mode
    li r0, 0
    st.b r0, [r1]
    li r1, saved_run_cursor
    ld.w r1, [r1]
    li r2, saved_program_end
    ld.w r2, [r2]
    li r4, saved_line_end
    ld.w r4, [r4]
    jmpa run_token

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
    li r4, 7340h
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
    li r1, 7340h
    ld.bu r3, [r1]
    li r4, 0030h
    blo r3, r4, direct_command
    li r4, 003ah
    bhs r3, r4, direct_command
    calla enter_program_line
    ; Program editing is quiet: the entered line is already echoed, so return
    ; straight to the keyboard loop instead of printing another READY prompt.
    li r1, input_length
    li r0, 0
    st.b r0, [r1]
    jmpa input_loop
direct_command:
    calla tokenize_interactive_line
    li r3, 1
    bne r0, r3, direct_error
    li r1, 73c0h
    ld.bu r0, [r1]
    li r3, 00b0h
    bne r0, r3, direct_cls
    calla run_program
    ret
direct_cls:
    li r3, 00a0h
    bne r0, r3, direct_new
    li r0, 0
    out 0037h, r0
    calla present_current_mode
    ret
direct_new:
    li r3, 00b2h
    bne r0, r3, direct_list
    li r3, 4000h
    li r0, 0
    st.b r0, [r3]
    ret
direct_list:
    li r3, 00b1h
    bne r0, r3, direct_cont
    calla list_program
    ret
direct_cont:
    li r3, 00b9h
    bne r0, r3, direct_error
    bne r2, r3, direct_error
    li r3, continuation_valid
    ld.bu r5, [r3]
    li r6, 1
    bne r5, r6, direct_error
    li r5, 0
    st.b r5, [r3]
    li r1, continuation_cursor
    ld.w r1, [r1]
    li r2, continuation_program_end
    ld.w r2, [r2]
    li r4, continuation_line_end
    ld.w r4, [r4]
    jmpa run_token
direct_error:
    li r1, syntax_text
    calla puts
    ret

; Lists the record forms accepted by the in-guest editor. Packed programs
; with newer statements still retain their line number and show '?' until the
; corresponding detokenizer is added.
list_program:
    li r1, 4000h
    ld.bu r0, [r1]
    li r3, 0042h
    bne r0, r3, no_program
    ld.w r2, [r1 + 6]
    li r3, 400ah
    add r2, r2, r3
    li r3, list_end_address
    st.w r2, [r3]
    li r1, 400ah
list_line:
    li r3, list_end_address
    ld.w r2, [r3]
    beq r1, r2, list_done
    ld.w r0, [r1]
    calla print_integer
    li r0, 0020h
    calla putc
    ld.w r3, [r1 + 2]
    li r4, 4
    add r4, r4, r1
    add r6, r4, r3
    li r7, list_next
    st.w r6, [r7]
    jmpa list_tokens
list_tokens:
    li r7, list_next
    ld.w r7, [r7]
    beq r4, r7, list_advance
    ld.bu r0, [r4]
    li r3, 1
    add r4, r4, r3
    li r3, 0080h
    blo r0, r3, list_raw_character
    li r3, 0082h
    beq r0, r3, list_integer_token
    li r3, 0083h
    beq r0, r3, list_string_token
    li r3, 0084h
    beq r0, r3, list_variable_token
    li r6, token_keyword_table
list_keyword_find:
    ld.bu r3, [r6]
    li r5, 0
    beq r3, r5, list_unknown
    beq r0, r3, list_keyword_emit
    ld.bu r3, [r6 + 1]
    li r5, 2
    add r3, r3, r5
    add r6, r6, r3
    jmpa list_keyword_find
list_keyword_emit:
    ld.bu r5, [r6 + 1]
    li r3, 2
    add r6, r6, r3
list_keyword_chars:
    li r3, 0
    beq r5, r3, list_keyword_space
    ld.bu r0, [r6]
    calla putc
    li r3, 1
    add r6, r6, r3
    sub r5, r5, r3
    jmpa list_keyword_chars
list_keyword_space:
    li r0, 0020h
    calla putc
    jmpa list_tokens
list_raw_character:
    calla putc
    jmpa list_tokens
list_integer_token:
    ld.w r0, [r4]
    li r3, 2
    add r4, r4, r3
    calla print_integer
    jmpa list_tokens
list_string_token:
    li r0, 0022h
    calla putc
    ld.bu r5, [r4]
    li r3, 1
    add r4, r4, r3
list_string_chars:
    li r3, 0
    beq r5, r3, list_string_close
    ld.bu r0, [r4]
    calla putc
    li r3, 1
    add r4, r4, r3
    sub r5, r5, r3
    jmpa list_string_chars
list_string_close:
    li r0, 0022h
    calla putc
    jmpa list_tokens
list_variable_token:
    ld.bu r5, [r4]
    li r3, 1
    add r4, r4, r3
    mov r0, r5
    li r3, 001fh
    and r0, r0, r3
    li r3, 0041h
    add r0, r0, r3
    calla putc
    mov r0, r5
    li r3, 00c0h
    and r0, r0, r3
    li r3, 0040h
    beq r0, r3, list_variable_integer
    li r3, 0080h
    bne r0, r3, list_tokens
    li r0, 0024h
    calla putc
    jmpa list_tokens
list_variable_integer:
    li r0, 0025h
    calla putc
    jmpa list_tokens
list_unknown:
    li r0, 003fh
    calla putc
list_advance:
    calla newline
    li r7, list_next
    ld.w r1, [r7]
    jmpa list_line
list_done:
    ret

; In-guest program editor. It parses a decimal line number, tokenizes the
; remaining Microsoft-style BASIC statement, and updates the B16P store.
enter_program_line:
    li r1, 7340h
    li r0, 0
parse_line_number:
    ld.bu r3, [r1]
    li r4, 0030h
    blo r3, r4, line_number_done
    li r4, 003ah
    bhs r3, r4, line_number_done
    li r4, 10
    mul r0, r0, r4
    li r4, 0030h
    sub r3, r3, r4
    add r0, r0, r3
    li r4, 1
    add r1, r1, r4
    jmpa parse_line_number
line_number_done:
    li r4, stored_line_number
    st.w r0, [r4]
skip_line_spaces:
    ld.bu r3, [r1]
    li r4, 0020h
    bne r3, r4, parse_statement
    li r4, 1
    add r1, r1, r4
    jmpa skip_line_spaces
parse_statement:
    ld.bu r3, [r1]
    li r4, 0
    beq r3, r4, delete_program_line
    calla tokenize_interactive_line
    li r3, 0
    beq r0, r3, direct_error
    mov r1, r0
    li r0, 73c0h
    ; append_raw_record expects R0=length and R1=source.
    mov r3, r1
    mov r1, r0
    mov r0, r3
    calla append_raw_record
    ret

; Converts the remaining ASCII input line at R1 into the B16P token stream
; used by Open16A-BASIC-PACK. R0 returns byte count, or zero on invalid input.
tokenize_interactive_line:
    li r2, token_cursor
    st.w r1, [r2]
    li r2, token_out
    li r3, 73c0h
    st.w r3, [r2]
    li r2, token_count
    li r3, 0
    st.w r3, [r2]
tokenize_next:
    li r1, token_cursor
    ld.w r1, [r1]
    ld.bu r0, [r1]
    li r2, 0
    beq r0, r2, tokenize_done
    li r2, 0020h
    beq r0, r2, tokenize_space
    li r2, 0022h
    beq r0, r2, tokenize_string
    li r2, 0030h
    blo r0, r2, tokenize_symbol
    li r2, 003ah
    blo r0, r2, tokenize_integer
    li r2, 0041h
    blo r0, r2, tokenize_symbol
    li r2, 005bh
    blo r0, r2, tokenize_word
    li r2, 0061h
    blo r0, r2, tokenize_symbol
    li r2, 007bh
    blo r0, r2, tokenize_word
    jmpa tokenize_error
tokenize_space:
    li r2, 1
    add r1, r1, r2
    li r2, token_cursor
    st.w r1, [r2]
    jmpa tokenize_next
tokenize_integer:
    li r4, 0
tokenize_integer_digit:
    ld.bu r0, [r1]
    li r2, 0030h
    blo r0, r2, tokenize_integer_emit
    li r2, 003ah
    bhs r0, r2, tokenize_integer_emit
    li r2, 10
    mul r4, r4, r2
    li r2, 0030h
    sub r0, r0, r2
    add r4, r4, r0
    li r2, 1
    add r1, r1, r2
    jmpa tokenize_integer_digit
tokenize_integer_emit:
    li r2, token_number
    st.w r4, [r2]
    li r2, token_cursor
    st.w r1, [r2]
    li r0, 0082h
    calla token_emit
    li r2, token_number
    ld.w r4, [r2]
    mov r0, r4
    li r2, 8
    shr r0, r0, r2
    calla token_emit
    li r2, token_number
    ld.w r0, [r2]
    calla token_emit
    jmpa tokenize_next
tokenize_string:
    li r2, 1
    add r1, r1, r2
    mov r4, r1
    li r5, 0
tokenize_string_count:
    ld.bu r0, [r1]
    li r2, 0
    beq r0, r2, tokenize_error
    li r2, 0022h
    beq r0, r2, tokenize_string_emit
    li r2, 1
    add r1, r1, r2
    add r5, r5, r2
    li r2, 00ffh
    bne r5, r2, tokenize_string_count
    jmpa tokenize_error
tokenize_string_emit:
    li r2, token_cursor
    li r3, 1
    add r1, r1, r3
    st.w r1, [r2]
    li r0, 0083h
    calla token_emit
    mov r0, r5
    calla token_emit
tokenize_string_copy:
    li r2, 0
    beq r5, r2, tokenize_next
    ld.bu r0, [r4]
    calla token_emit
    li r2, 1
    add r4, r4, r2
    sub r5, r5, r2
    jmpa tokenize_string_copy
tokenize_word:
    li r2, token_word_start
    st.w r1, [r2]
    li r4, 0
tokenize_word_letters:
    ld.bu r0, [r1]
    calla token_to_lower
    li r2, 0061h
    blo r0, r2, tokenize_word_suffix
    li r2, 007bh
    bhs r0, r2, tokenize_word_suffix
    li r2, 1
    add r1, r1, r2
    add r4, r4, r2
    jmpa tokenize_word_letters
tokenize_word_suffix:
    ld.bu r0, [r1]
    li r2, 0025h
    beq r0, r2, tokenize_word_take_suffix
    li r2, 0024h
    bne r0, r2, tokenize_word_ready
tokenize_word_take_suffix:
    li r2, 1
    add r1, r1, r2
    add r4, r4, r2
tokenize_word_ready:
    li r2, token_word_length
    st.w r4, [r2]
    li r2, token_cursor
    st.w r1, [r2]
    li r2, 1
    beq r4, r2, tokenize_variable_float
    li r2, 2
    bne r4, r2, tokenize_keyword
    li r2, token_word_start
    ld.w r1, [r2]
    ld.bu r0, [r1 + 1]
    li r2, 0025h
    beq r0, r2, tokenize_variable_integer
    li r2, 0024h
    beq r0, r2, tokenize_variable_string
    jmpa tokenize_keyword
tokenize_variable_float:
    li r2, 0
    jmpa tokenize_variable_emit
tokenize_variable_integer:
    li r2, 0040h
    jmpa tokenize_variable_emit
tokenize_variable_string:
    li r2, 0080h
tokenize_variable_emit:
    li r3, token_variable_type
    st.b r2, [r3]
    li r1, token_word_start
    ld.w r1, [r1]
    ld.bu r0, [r1]
    calla token_to_lower
    li r1, 0061h
    sub r0, r0, r1
    li r1, token_variable_type
    ld.bu r1, [r1]
    or r0, r0, r1
    li r1, token_variable_value
    st.b r0, [r1]
    li r0, 0084h
    calla token_emit
    li r0, token_variable_value
    ld.bu r0, [r0]
    calla token_emit
    jmpa tokenize_next
tokenize_keyword:
    li r6, token_keyword_table
tokenize_keyword_entry:
    ld.bu r0, [r6]
    li r2, 0
    beq r0, r2, tokenize_error
    ld.bu r2, [r6 + 1]
    li r3, token_word_length
    ld.w r3, [r3]
    bne r2, r3, tokenize_keyword_skip
    li r1, token_word_start
    ld.w r1, [r1]
    li r4, 0
tokenize_keyword_compare:
    beq r4, r3, tokenize_keyword_found
    li r5, 2
    add r5, r5, r6
    add r5, r5, r4
    ld.bu r5, [r5]
    ld.bu r2, [r1]
    mov r0, r2
    calla token_to_lower
    bne r0, r5, tokenize_keyword_skip
    li r2, 1
    add r1, r1, r2
    add r4, r4, r2
    jmpa tokenize_keyword_compare
tokenize_keyword_found:
    ld.bu r0, [r6]
    calla token_emit
    li r1, 009ch
    bne r0, r1, tokenize_next
    ; REM owns the rest of the line verbatim.
    li r1, token_cursor
    ld.w r1, [r1]
tokenize_rem_copy:
    ld.bu r0, [r1]
    li r2, 0
    beq r0, r2, tokenize_done
    calla token_emit
    li r2, 1
    add r1, r1, r2
    jmpa tokenize_rem_copy
tokenize_keyword_skip:
    ld.bu r2, [r6 + 1]
    li r7, 2
    add r2, r2, r7
    add r6, r6, r2
    jmpa tokenize_keyword_entry
tokenize_symbol:
    li r2, 002bh
    beq r0, r2, tokenize_symbol_emit
    li r2, 002dh
    beq r0, r2, tokenize_symbol_emit
    li r2, 002ah
    beq r0, r2, tokenize_symbol_emit
    li r2, 002fh
    beq r0, r2, tokenize_symbol_emit
    li r2, 003dh
    beq r0, r2, tokenize_symbol_emit
    li r2, 003ch
    beq r0, r2, tokenize_symbol_emit
    li r2, 003eh
    beq r0, r2, tokenize_symbol_emit
    li r2, 0028h
    beq r0, r2, tokenize_symbol_emit
    li r2, 0029h
    beq r0, r2, tokenize_symbol_emit
    li r2, 002ch
    beq r0, r2, tokenize_symbol_emit
    li r2, 003bh
    beq r0, r2, tokenize_symbol_emit
    li r2, 003ah
    bne r0, r2, tokenize_error
tokenize_symbol_emit:
    calla token_emit
    li r1, token_cursor
    ld.w r1, [r1]
    li r2, 1
    add r1, r1, r2
    li r2, token_cursor
    st.w r1, [r2]
    jmpa tokenize_next
tokenize_done:
    li r0, token_count
    ld.w r0, [r0]
    ret
tokenize_error:
    li r0, 0
    ret

; R0=ASCII byte. Emits it to the token buffer at 73C0h, returning normally or setting a
; zero token count on capacity overflow.
token_emit:
    li r1, token_count
    ld.w r2, [r1]
    li r3, 007fh
    bhs r2, r3, token_emit_overflow
    li r3, token_out
    ld.w r3, [r3]
    st.b r0, [r3]
    li r4, 1
    add r3, r3, r4
    li r4, token_out
    st.w r3, [r4]
    li r4, 1
    add r2, r2, r4
    li r1, token_count
    st.w r2, [r1]
    ret
token_emit_overflow:
    li r1, token_count
    li r2, 0
    st.w r2, [r1]
    ret

; R0=ASCII char, converts A-Z to a-z.
token_to_lower:
    li r7, 0041h
    blo r0, r7, token_to_lower_done
    li r7, 005bh
    bhs r0, r7, token_to_lower_done
    li r7, 0020h
    add r0, r0, r7
token_to_lower_done:
    ret
; R0=token byte length, R1=token byte source. Program records remain sorted by
; line number and use exactly the same B16P layout as the host packer.
append_raw_record:
    li r3, record_token_length
    st.w r0, [r3]
    li r3, record_string_source
    st.w r1, [r3]
append_program_record_begin:
    li r3, continuation_valid
    li r4, 0
    st.b r4, [r3]
    li r3, 4000h
    ld.bu r4, [r3]
    li r7, 0042h
    beq r4, r7, program_header_ready
    li r4, 0042h
    st.b r4, [r3]
    li r4, 0031h
    st.b r4, [r3 + 1]
    li r4, 0036h
    st.b r4, [r3 + 2]
    li r4, 0050h
    st.b r4, [r3 + 3]
    li r4, 1
    st.b r4, [r3 + 4]
    li r4, 0
    st.b r4, [r3 + 5]
    st.w r4, [r3 + 6]
    st.w r4, [r3 + 8]
program_header_ready:
    ; Find insertion point and remove the old record when editing a line.
    li r4, 400ah
    ld.w r5, [r3 + 6]
    add r5, r5, r4
find_insert_position:
    beq r4, r5, insert_position_found
    ld.w r6, [r4]
    li r7, stored_line_number
    ld.w r7, [r7]
    blt r7, r6, insert_position_found
    beq r7, r6, replace_program_record
    ld.w r6, [r4 + 2]
    li r7, 4
    add r6, r6, r7
    add r4, r4, r6
    jmpa find_insert_position
replace_program_record:
    mov r0, r4
    calla remove_program_record
    mov r4, r0
insert_position_found:
    li r3, insert_position
    st.w r4, [r3]
    li r3, record_token_length
    ld.w r0, [r3]
    li r1, 4
    add r0, r0, r1
    li r3, 4006h
    ld.w r1, [r3]
    li r2, 400ah
    add r1, r1, r2
    li r3, program_old_end
    st.w r1, [r3]
    add r1, r1, r0
    li r2, 7000h
    bgt r1, r2, program_full
    li r2, insert_position
    ld.w r2, [r2]
    li r3, program_old_end
    ld.w r1, [r3]
shift_program_right:
    beq r1, r2, write_program_record
    li r3, 1
    sub r1, r1, r3
    ld.bu r4, [r1]
    li r5, record_token_length
    ld.w r5, [r5]
    li r6, 4
    add r5, r5, r6
    add r5, r5, r1
    st.b r4, [r5]
    jmpa shift_program_right
write_program_record:
    li r4, insert_position
    ld.w r4, [r4]
    li r5, stored_line_number
    ld.w r5, [r5]
    st.w r5, [r4]
    li r5, record_token_length
    ld.w r5, [r5]
    st.w r5, [r4 + 2]
    li r6, 4
    add r4, r4, r6
    li r5, record_string_source
    ld.w r5, [r5]
    li r6, record_token_length
    ld.w r6, [r6]
write_raw_bytes:
    li r7, 0
    beq r6, r7, write_record_finish
    ld.bu r7, [r5]
    st.b r7, [r4]
    li r7, 1
    add r5, r5, r7
    add r4, r4, r7
    sub r6, r6, r7
    jmpa write_raw_bytes
write_record_finish:
    li r4, 4006h
    ld.w r7, [r4]
    li r3, record_token_length
    ld.w r3, [r3]
    li r5, 4
    add r3, r3, r5
    add r7, r7, r3
    st.w r7, [r4]
    li r4, 4008h
    ld.w r7, [r4]
    li r3, 1
    add r7, r7, r3
    st.w r7, [r4]
    ret

; R0=record address. Removes it and leaves R0 at the same insertion address.
remove_program_record:
    mov r4, r0
    ld.w r5, [r4 + 2]
    li r6, 4
    add r5, r5, r6
    mov r1, r4
    add r1, r1, r5
    li r2, 4006h
    ld.w r3, [r2]
    li r6, 400ah
    add r3, r3, r6
remove_program_copy:
    beq r1, r3, remove_program_finish
    ld.bu r6, [r1]
    st.b r6, [r4]
    li r6, 1
    add r1, r1, r6
    add r4, r4, r6
    jmpa remove_program_copy
remove_program_finish:
    li r2, 4006h
    ld.w r3, [r2]
    sub r3, r3, r5
    st.w r3, [r2]
    li r2, 4008h
    ld.w r3, [r2]
    li r6, 1
    sub r3, r3, r6
    st.w r3, [r2]
    ret

delete_program_line:
    li r3, 4000h
    ld.bu r4, [r3]
    li r5, 0042h
    bne r4, r5, delete_line_done
    li r0, 400ah
    ld.w r1, [r3 + 6]
    add r1, r1, r0
delete_line_scan:
    beq r0, r1, delete_line_done
    ld.w r4, [r0]
    li r5, stored_line_number
    ld.w r5, [r5]
    beq r4, r5, delete_line_found
    blt r5, r4, delete_line_done
    ld.w r4, [r0 + 2]
    li r5, 4
    add r4, r4, r5
    add r0, r0, r4
    jmpa delete_line_scan
delete_line_found:
    calla remove_program_record
delete_line_done:
    ret

program_full:
    li r1, program_full_text
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

; Interpret the B16P program header at 4000h and dispatch Open16A BASIC 1.1.
run_program:
    li r3, continuation_valid
    li r5, 0
    st.b r5, [r3]
    li r3, gosub_depth
    st.b r5, [r3]
    li r3, for_depth
    st.b r5, [r3]
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
    li r3, break_requested
    ld.bu r3, [r3]
    li r5, 0
    beq r3, r5, run_token_dispatch
    li r3, break_requested
    st.b r5, [r3]
    li r1, break_text
    calla puts
    jmpa run_done
run_token_dispatch:
    beq r1, r4, run_line
    ld.bu r0, [r1]
    li r3, 1
    add r1, r1, r3
    li r3, 003ah
    beq r0, r3, run_token
    li r3, 0084h
    beq r0, r3, run_implicit_let
    li r3, 0090h
    blo r0, r3, run_syntax
    li r5, 00c2h
    bhs r0, r5, run_syntax
    sub r0, r0, r3
    add r0, r0, r0
    li r3, statement_dispatch_table
    add r3, r3, r0
    ld.w r3, [r3]
    li r5, 0
    beq r3, r5, run_syntax
    jmp r3
run_print:
    beq r1, r4, print_newline
    ld.bu r0, [r1]
    li r3, 0083h
    beq r0, r3, print_string_begin
    li r3, 0084h
    bne r0, r3, run_print_numeric
    ld.bu r3, [r1 + 1]
    li r5, 0080h
    and r3, r3, r5
    beq r3, r5, print_string_variable
run_print_numeric:
    calla read_integer
    calla print_integer
    jmpa print_after_item

run_implicit_let:
    li r3, 1
    sub r1, r1, r3
    jmpa run_let

run_input:
    ld.bu r3, [r1]
    li r5, 0083h
    bne r3, r5, run_input_variable
    li r3, 1
    add r1, r1, r3
    ld.bu r5, [r1]
    add r1, r1, r3
run_input_prompt_string:
    li r3, 0
    beq r5, r3, run_input_variable
    ld.bu r0, [r1]
    calla putc
    li r3, 1
    add r1, r1, r3
    sub r5, r5, r3
    jmpa run_input_prompt_string
run_input_variable:
    ld.bu r3, [r1]
    li r5, 003bh
    beq r3, r5, run_input_skip_separator
    li r5, 002ch
    bne r3, r5, run_input_expect_variable
run_input_skip_separator:
    li r3, 1
    add r1, r1, r3
    ld.bu r3, [r1]
run_input_expect_variable:
    li r5, 0084h
    bne r3, r5, run_syntax
    li r3, 1
    add r1, r1, r3
    ld.bu r5, [r1]
    li r3, input_target_variable
    st.b r5, [r3]
    li r3, 1
    add r1, r1, r3
    li r3, saved_run_cursor
    st.w r1, [r3]
    li r3, saved_program_end
    st.w r2, [r3]
    li r3, saved_line_end
    st.w r4, [r3]
    li r3, input_mode
    li r5, 1
    st.b r5, [r3]
    li r3, input_length
    li r5, 0
    st.b r5, [r3]
    li r1, input_prompt_text
    calla puts
    jmpa input_loop

parse_runtime_input:
    li r1, 7340h
    li r0, 0
    li r5, 0
    ld.bu r3, [r1]
    li r6, 002dh
    bne r3, r6, parse_runtime_digits
    li r5, 1
    li r3, 1
    add r1, r1, r3
parse_runtime_digits:
    ld.bu r3, [r1]
    li r6, 0
    beq r3, r6, parse_runtime_done
    li r6, 0030h
    blo r3, r6, parse_runtime_invalid
    li r6, 003ah
    bhs r3, r6, parse_runtime_invalid
    li r6, 10
    mul r0, r0, r6
    li r6, 0030h
    sub r3, r3, r6
    add r0, r0, r3
    li r3, 1
    add r1, r1, r3
    jmpa parse_runtime_digits
parse_runtime_done:
    li r3, 0
    beq r5, r3, parse_runtime_store
    neg r0, r0
parse_runtime_store:
    li r5, input_target_variable
    ld.bu r5, [r5]
    mov r3, r5
    li r6, 0080h
    and r3, r3, r6
    beq r3, r6, parse_runtime_store_string
    calla basic_store_variable
    ret
parse_runtime_store_string:
    calla basic_string_address
    li r1, 7340h
    li r5, 0
parse_runtime_string_count:
    ld.bu r3, [r1]
    li r7, 0
    beq r3, r7, parse_runtime_string_ready
    li r7, 31
    beq r5, r7, parse_runtime_string_ready
    li r7, 1
    add r1, r1, r7
    add r5, r5, r7
    jmpa parse_runtime_string_count
parse_runtime_string_ready:
    st.b r5, [r6]
    li r7, 1
    add r6, r6, r7
    li r1, 7340h
parse_runtime_string_copy:
    li r7, 0
    beq r5, r7, parse_runtime_string_done
    ld.bu r3, [r1]
    st.b r3, [r6]
    li r7, 1
    add r1, r1, r7
    add r6, r6, r7
    sub r5, r5, r7
    jmpa parse_runtime_string_copy
parse_runtime_string_done:
    ret
parse_runtime_invalid:
    li r0, 0
    jmpa parse_runtime_store

run_data:
    mov r1, r4
    jmpa run_line

run_restore:
    li r3, data_cursor
    li r5, 0
    st.w r5, [r3]
    li r3, data_scan_pointer
    li r5, 400ah
    st.w r5, [r3]
    jmpa run_token

run_read:
run_read_variable:
    ld.bu r3, [r1]
    li r5, 0084h
    bne r3, r5, run_syntax
    li r3, 1
    add r1, r1, r3
    ld.bu r5, [r1]
    li r3, read_target_variable
    st.b r5, [r3]
    li r3, 1
    add r1, r1, r3
    li r3, saved_run_cursor
    st.w r1, [r3]
    calla read_next_data_value
    li r5, read_target_variable
    ld.bu r5, [r5]
    calla basic_store_variable
    li r1, saved_run_cursor
    ld.w r1, [r1]
    ld.bu r3, [r1]
    li r5, 002ch
    bne r3, r5, run_token
    li r3, 1
    add r1, r1, r3
    jmpa run_read_variable

read_next_data_value:
    li r3, data_cursor
    ld.w r1, [r3]
    li r5, 0
    beq r1, r5, find_data_record
    li r3, data_line_end
    ld.w r5, [r3]
    beq r1, r5, find_data_record
    ld.bu r3, [r1]
    li r5, 002ch
    bne r3, r5, read_data_token
    li r3, 1
    add r1, r1, r3
read_data_token:
    li r6, 0
    ld.bu r3, [r1]
    li r5, 002dh
    bne r3, r5, read_data_integer
    li r6, 1
    li r3, 1
    add r1, r1, r3
read_data_integer:
    ld.bu r3, [r1]
    li r5, 0082h
    bne r3, r5, run_syntax
    ld.w r0, [r1 + 1]
    li r3, 3
    add r1, r1, r3
    li r3, data_cursor
    st.w r1, [r3]
    li r3, 0
    beq r6, r3, read_data_done
    neg r0, r0
read_data_done:
    ret
find_data_record:
    li r3, data_scan_pointer
    ld.w r1, [r3]
    li r5, 0
    bne r1, r5, find_data_loop
    li r1, 400ah
find_data_loop:
    beq r1, r2, run_syntax
    ld.w r5, [r1 + 2]
    li r6, 4
    add r1, r1, r6
    add r6, r1, r5
    li r3, data_scan_pointer
    st.w r6, [r3]
    ld.bu r3, [r1]
    li r5, 00b6h
    beq r3, r5, find_data_found
    mov r1, r6
    jmpa find_data_loop
find_data_found:
    li r3, 1
    add r1, r1, r3
    li r3, data_cursor
    st.w r1, [r3]
    li r3, data_line_end
    st.w r6, [r3]
    jmpa read_next_data_value
print_string_begin:
    li r3, 1
    add r1, r1, r3
    ld.bu r5, [r1]
    add r1, r1, r3
    mov r6, r1
    add r1, r1, r5
    jmpa print_string
print_string_variable:
    li r3, 1
    add r1, r1, r3
    ld.bu r5, [r1]
    add r1, r1, r3
    push r1
    calla basic_string_address
    ld.bu r5, [r6]
    add r6, r6, r3
    pop r1
print_string:
    li r3, 0
    beq r5, r3, print_after_item
    ld.bu r0, [r6]
    li r3, 1
    add r6, r6, r3
    calla putc
    li r3, 1
    sub r5, r5, r3
    jmpa print_string
print_after_item:
    beq r1, r4, print_newline
    ld.bu r3, [r1]
    li r5, 003bh
    beq r3, r5, print_semicolon
    li r5, 002ch
    beq r3, r5, print_comma
    jmpa print_newline
print_semicolon:
    li r3, 1
    add r1, r1, r3
    beq r1, r4, run_token
    jmpa run_print
print_comma:
    li r0, 0020h
    calla putc
    li r3, 1
    add r1, r1, r3
    jmpa run_print
print_newline:
    calla newline
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
    li r3, 0080h
    and r3, r3, r5
    li r6, 0
    bne r3, r6, run_syntax
    li r6, 001fh
    and r5, r5, r6
    add r5, r5, r5
    ld.bu r3, [r1 - 1]
    li r6, 0040h
    and r3, r3, r6
    beq r3, r6, print_integer_variable_integer
    li r6, 7474h
    jmpa print_integer_variable_load
print_integer_variable_integer:
    li r6, 7440h
print_integer_variable_load:
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
    li r3, assignment_variable
    st.b r5, [r3]
    li r3, 1
    add r1, r1, r3
    li r3, assignment_is_array
    li r6, 0
    st.b r6, [r3]
    ld.bu r3, [r1]
    li r6, 0028h
    bne r3, r6, run_let_equals
    li r3, 1
    add r1, r1, r3
    calla read_integer
    li r3, 16
    bhs r0, r3, run_syntax
    li r5, assignment_variable
    ld.bu r5, [r5]
    calla basic_array_address
    li r3, assignment_array_address
    st.w r6, [r3]
    ld.bu r3, [r1]
    li r6, 0029h
    bne r3, r6, run_syntax
    li r3, 1
    add r1, r1, r3
    li r3, assignment_is_array
    li r6, 1
    st.b r6, [r3]
run_let_equals:
    ld.bu r0, [r1]
    li r3, 1
    add r1, r1, r3
    li r3, 003dh
    bne r0, r3, run_syntax
    li r3, assignment_variable
    ld.bu r5, [r3]
    li r3, 0080h
    and r3, r3, r5
    li r6, 0080h
    beq r3, r6, run_let_string
    calla read_integer
    li r3, assignment_is_array
    ld.bu r3, [r3]
    li r6, 1
    beq r3, r6, run_let_array_store
    li r3, assignment_variable
    ld.bu r5, [r3]
    mov r7, r5
    li r3, 0080h
    and r3, r3, r5
    li r6, 0
    bne r3, r6, run_syntax
    li r6, 001fh
    and r5, r5, r6
    add r5, r5, r5
    li r3, 0040h
    and r3, r3, r7
    li r6, 0040h
    beq r3, r6, run_let_integer
    li r6, 7474h
    jmpa run_let_store
run_let_integer:
    li r6, 7440h
run_let_store:
    add r6, r6, r5
    st.w r0, [r6]
    jmpa run_token
run_let_array_store:
    li r3, assignment_array_address
    ld.w r3, [r3]
    st.w r0, [r3]
    jmpa run_token

run_let_string:
    calla basic_string_address
    li r3, string_destination
    st.w r6, [r3]
    ld.bu r3, [r1]
    li r5, 0083h
    beq r3, r5, run_let_string_literal
    li r5, 0084h
    bne r3, r5, run_syntax
    li r3, 1
    add r1, r1, r3
    ld.bu r5, [r1]
    add r1, r1, r3
    calla basic_string_address
    ld.bu r5, [r6]
    add r6, r6, r3
    jmpa run_let_string_copy
run_let_string_literal:
    li r3, 1
    add r1, r1, r3
    ld.bu r5, [r1]
    add r1, r1, r3
    mov r6, r1
    add r1, r1, r5
run_let_string_copy:
    li r3, 31
    ble r5, r3, run_let_string_length_ready
    mov r5, r3
run_let_string_length_ready:
    li r3, string_destination
    ld.w r3, [r3]
    st.b r5, [r3]
    li r7, 1
    add r3, r3, r7
run_let_string_bytes:
    li r7, 0
    beq r5, r7, run_token
    ld.bu r0, [r6]
    st.b r0, [r3]
    li r7, 1
    add r6, r6, r7
    add r3, r3, r7
    sub r5, r5, r7
    jmpa run_let_string_bytes

run_goto:
    calla read_integer
    jmpa goto_line

run_gosub:
    calla read_integer
    push r0
    li r3, gosub_depth
    ld.bu r5, [r3]
    li r6, 8
    bhs r5, r6, run_gosub_overflow
    add r6, r5, r5
    li r7, 74a8h
    add r7, r7, r6
    st.w r4, [r7]
    li r6, 1
    add r5, r5, r6
    st.b r5, [r3]
    pop r0
    jmpa goto_line
run_gosub_overflow:
    pop r0
    jmpa run_syntax

run_return:
    li r3, gosub_depth
    ld.bu r5, [r3]
    li r6, 0
    beq r5, r6, run_syntax
    li r6, 1
    sub r5, r5, r6
    st.b r5, [r3]
    add r6, r5, r5
    li r7, 74a8h
    add r7, r7, r6
    ld.w r1, [r7]
    jmpa run_line

run_for:
    ld.bu r3, [r1]
    li r5, 0084h
    bne r3, r5, run_syntax
    li r3, 1
    add r1, r1, r3
    ld.bu r5, [r1]
    li r3, for_new_variable
    st.b r5, [r3]
    li r3, 1
    add r1, r1, r3
    ld.bu r3, [r1]
    li r5, 003dh
    bne r3, r5, run_syntax
    li r3, 1
    add r1, r1, r3
    calla read_integer
    li r5, for_new_variable
    ld.bu r5, [r5]
    calla basic_store_variable
    ld.bu r3, [r1]
    li r5, 0099h
    bne r3, r5, run_syntax
    li r3, 1
    add r1, r1, r3
    calla read_integer
    li r3, for_new_limit
    st.w r0, [r3]
    li r3, for_new_step
    li r5, 1
    st.w r5, [r3]
    ld.bu r3, [r1]
    li r5, 009ah
    bne r3, r5, run_for_push
    li r3, 1
    add r1, r1, r3
    calla read_integer
    li r3, for_new_step
    st.w r0, [r3]
run_for_push:
    li r3, for_depth
    ld.bu r7, [r3]
    li r5, 8
    bhs r7, r5, run_syntax
    li r5, 74b8h
    add r5, r5, r7
    li r6, for_new_variable
    ld.bu r6, [r6]
    st.b r6, [r5]
    add r6, r7, r7
    li r5, 74c0h
    add r5, r5, r6
    li r0, for_new_limit
    ld.w r0, [r0]
    st.w r0, [r5]
    li r5, 74d0h
    add r5, r5, r6
    li r0, for_new_step
    ld.w r0, [r0]
    st.w r0, [r5]
    li r5, 74e0h
    add r5, r5, r6
    st.w r4, [r5]
    li r5, 1
    add r7, r7, r5
    st.b r7, [r3]
    jmpa run_token

run_next:
    li r3, for_depth
    ld.bu r7, [r3]
    li r5, 0
    beq r7, r5, run_syntax
    li r5, 1
    sub r7, r7, r5
    li r5, 74b8h
    add r5, r5, r7
    ld.bu r5, [r5]
    ld.bu r6, [r1]
    li r0, 0084h
    bne r6, r0, run_next_increment
    li r0, 1
    add r1, r1, r0
    ld.bu r6, [r1]
    bne r5, r6, run_syntax
    add r1, r1, r0
run_next_increment:
    li r3, for_active_variable
    st.b r5, [r3]
    calla basic_load_variable
    add r6, r7, r7
    li r3, 74d0h
    add r3, r3, r6
    ld.w r3, [r3]
    add r0, r0, r3
    li r5, for_active_variable
    ld.bu r5, [r5]
    calla basic_store_variable
    add r6, r7, r7
    li r3, 74c0h
    add r3, r3, r6
    ld.w r5, [r3]
    li r3, 74d0h
    add r3, r3, r6
    ld.w r3, [r3]
    li r6, 0
    blt r3, r6, run_next_negative
    bgt r0, r5, run_next_done
    jmpa run_next_continue
run_next_negative:
    blt r0, r5, run_next_done
run_next_continue:
    add r6, r7, r7
    li r3, 74e0h
    add r3, r3, r6
    ld.w r1, [r3]
    jmpa run_line
run_next_done:
    li r3, for_depth
    st.b r7, [r3]
    jmpa run_token

; R5=encoded variable, returns/stores its 16-bit numeric value in R0.
basic_load_variable:
    mov r3, r5
    li r6, 0080h
    and r3, r3, r6
    li r6, 0
    bne r3, r6, read_integer_invalid
    mov r3, r5
    li r6, 001fh
    and r3, r3, r6
    add r3, r3, r3
    li r6, 0040h
    and r5, r5, r6
    beq r5, r6, basic_load_integer
    li r6, 7474h
    jmpa basic_load_value
basic_load_integer:
    li r6, 7440h
basic_load_value:
    add r6, r6, r3
    ld.w r0, [r6]
    ret

basic_store_variable:
    push r0
    calla basic_variable_address
    pop r0
    st.w r0, [r6]
    ret
basic_variable_address:
    mov r3, r5
    li r6, 001fh
    and r3, r3, r6
    add r3, r3, r3
    li r6, 0040h
    and r5, r5, r6
    beq r5, r6, basic_variable_integer
    li r6, 7474h
    jmpa basic_variable_address_done
basic_variable_integer:
    li r6, 7440h
basic_variable_address_done:
    add r6, r6, r3
    ret

basic_string_address:
    li r6, 001fh
    and r5, r5, r6
    li r6, 5
    shl r5, r5, r6
    li r6, 7000h
    add r6, r6, r5
    ret
basic_array_address:
    push r0
    li r6, 001fh
    and r5, r5, r6
    li r6, 5
    shl r5, r5, r6
    pop r0
    add r0, r0, r0
    add r5, r5, r0
    li r6, 7800h
    add r6, r6, r5
    ret

run_poke:
    calla read_integer
    li r5, poke_address
    st.w r0, [r5]
    ld.bu r3, [r1]
    li r6, 1
    add r1, r1, r6
    li r6, 002ch
    bne r3, r6, run_syntax
    calla read_integer
    li r5, poke_address
    ld.w r5, [r5]
    st.b r0, [r5]
    jmpa run_token

run_if:
    calla read_integer
    li r5, if_left_value
    st.w r0, [r5]
    ld.bu r7, [r1]
    li r3, 1
    add r1, r1, r3
    li r5, if_operator
    st.b r7, [r5]
    li r5, 0
    li r3, if_second_operator
    st.b r5, [r3]
    ld.bu r5, [r1]
    li r3, 003dh
    beq r5, r3, if_take_second_operator
    li r3, 003eh
    beq r5, r3, if_take_second_operator
    jmpa if_read_right
if_take_second_operator:
    li r3, if_second_operator
    st.b r5, [r3]
    li r3, 1
    add r1, r1, r3
if_read_right:
    calla read_integer
    mov r6, r0
    li r5, if_left_value
    ld.w r5, [r5]
    li r3, if_operator
    ld.bu r7, [r3]
    li r3, 003dh
    beq r7, r3, if_equal
    li r3, 003ch
    beq r7, r3, if_less_dispatch
    li r3, 003eh
    beq r7, r3, if_greater_dispatch
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
if_less_dispatch:
    li r3, if_second_operator
    ld.bu r3, [r3]
    li r7, 003dh
    beq r3, r7, if_less_equal
    li r7, 003eh
    beq r3, r7, if_not_equal
    jmpa if_less
if_greater_dispatch:
    li r3, if_second_operator
    ld.bu r3, [r3]
    li r7, 003dh
    beq r3, r7, if_greater_equal
    jmpa if_greater
if_less_equal:
    ble r5, r6, if_true
    jmpa if_false
if_greater_equal:
    bge r5, r6, if_true
    jmpa if_false
if_not_equal:
    bne r5, r6, if_true
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

; Integer expression evaluator. It implements Microsoft BASIC precedence for
; unary +/-/NOT, */ and +-. Relational operators are consumed by IF itself.
read_integer:
    calla read_or_expression
    ret

read_or_expression:
    calla read_and_expression
read_or_loop:
    ld.bu r3, [r1]
    li r5, 00aeh
    bne r3, r5, read_or_done
    li r3, 1
    add r1, r1, r3
    push r0
    calla read_and_expression
    pop r3
    or r0, r3, r0
    jmpa read_or_loop
read_or_done:
    ret

read_and_expression:
    calla read_additive
read_and_loop:
    ld.bu r3, [r1]
    li r5, 00adh
    bne r3, r5, read_and_done
    li r3, 1
    add r1, r1, r3
    push r0
    calla read_additive
    pop r3
    and r0, r3, r0
    jmpa read_and_loop
read_and_done:
    ret

read_additive:
    calla read_multiplicative
read_additive_loop:
    ld.bu r3, [r1]
    li r5, 002bh
    beq r3, r5, read_add
    li r5, 002dh
    beq r3, r5, read_subtract
    ret
read_add:
    li r3, 1
    add r1, r1, r3
    push r0
    calla read_multiplicative
    pop r3
    add r0, r3, r0
    jmpa read_additive_loop
read_subtract:
    li r3, 1
    add r1, r1, r3
    push r0
    calla read_multiplicative
    pop r3
    sub r0, r3, r0
    jmpa read_additive_loop

read_multiplicative:
    calla read_unary
read_multiplicative_loop:
    ld.bu r3, [r1]
    li r5, 002ah
    beq r3, r5, read_multiply
    li r5, 002fh
    beq r3, r5, read_divide
    ret
read_multiply:
    li r3, 1
    add r1, r1, r3
    push r0
    calla read_unary
    pop r3
    mul r0, r3, r0
    jmpa read_multiplicative_loop
read_divide:
    li r3, 1
    add r1, r1, r3
    push r0
    calla read_unary
    mov r5, r0
    pop r3
    li r6, 0
    beq r5, r6, read_integer_invalid
    div r0, r3, r5
    jmpa read_multiplicative_loop

read_unary:
    ld.bu r0, [r1]
    li r3, 1
    li r5, 002bh
    beq r0, r5, read_unary_positive
    li r5, 002dh
    beq r0, r5, read_unary_negative
    li r5, 00afh
    beq r0, r5, read_unary_not
    jmpa read_primary
read_unary_positive:
    add r1, r1, r3
    jmpa read_unary
read_unary_negative:
    add r1, r1, r3
    calla read_unary
    neg r0, r0
    ret
read_unary_not:
    add r1, r1, r3
    calla read_unary
    not r0, r0
    ret

read_primary:
    ld.bu r0, [r1]
    li r3, 1
    add r1, r1, r3
    li r3, 0082h
    beq r0, r3, read_integer_literal
    li r3, 0084h
    beq r0, r3, read_integer_variable
    li r3, 0028h
    beq r0, r3, read_parenthesized
    li r3, 00b3h
    beq r0, r3, read_peek_function
    li r3, 00a3h
    beq r0, r3, read_abs_function
    li r3, 00a4h
    beq r0, r3, read_int_function
    li r3, 00a5h
    beq r0, r3, read_sgn_function
    li r3, 00a6h
    beq r0, r3, read_len_function
    li r3, 00ach
    beq r0, r3, read_val_function
    li r3, 00c2h
    beq r0, r3, read_inp_function
    li r3, 00c3h
    beq r0, r3, read_point_function
    jmpa read_integer_invalid
read_integer_variable:
    ld.bu r5, [r1]
    li r3, array_variable
    st.b r5, [r3]
    li r3, 1
    add r1, r1, r3
    ld.bu r3, [r1]
    li r6, 0028h
    beq r3, r6, read_integer_array
    li r6, 001fh
    and r5, r5, r6
    add r5, r5, r5
    ld.bu r3, [r1 - 1]
    li r6, 0040h
    and r3, r3, r6
    beq r3, r6, read_integer_variable_integer
    li r6, 7474h
    jmpa read_integer_variable_load
read_integer_variable_integer:
    li r6, 7440h
read_integer_variable_load:
    add r6, r6, r5
    ld.w r0, [r6]
    ret
read_integer_array:
    li r3, 1
    add r1, r1, r3
    calla read_integer
    li r3, 16
    bhs r0, r3, read_integer_invalid
    li r5, array_variable
    ld.bu r5, [r5]
    calla basic_array_address
    ld.bu r3, [r1]
    li r5, 0029h
    bne r3, r5, read_integer_invalid
    li r3, 1
    add r1, r1, r3
    ld.w r0, [r6]
    ret
read_integer_literal:
    ld.w r0, [r1]
    li r3, 2
    add r1, r1, r3
    ret
read_parenthesized:
    calla read_integer
    ld.bu r3, [r1]
    li r5, 0029h
    bne r3, r5, read_integer_invalid
    li r3, 1
    add r1, r1, r3
    ret
read_peek_function:
    ld.bu r3, [r1]
    li r5, 0028h
    bne r3, r5, read_integer_invalid
    li r3, 1
    add r1, r1, r3
    calla read_integer
    mov r5, r0
    ld.bu r3, [r1]
    li r6, 0029h
    bne r3, r6, read_integer_invalid
    li r3, 1
    add r1, r1, r3
    ld.bu r0, [r5]
    ret
read_abs_function:
    calla read_function_argument
    li r3, 0
    bge r0, r3, read_function_done
    neg r0, r0
read_function_done:
    ret
read_int_function:
    calla read_function_argument
    ret
read_sgn_function:
    calla read_function_argument
    li r3, 0
    beq r0, r3, read_function_done
    bgt r0, r3, read_sgn_positive
    li r0, -1
    ret
read_sgn_positive:
    li r0, 1
    ret
read_len_function:
    ld.bu r3, [r1]
    li r5, 0028h
    bne r3, r5, read_integer_invalid
    li r3, 1
    add r1, r1, r3
    ld.bu r3, [r1]
    li r5, 0083h
    beq r3, r5, read_len_literal
    li r5, 0084h
    bne r3, r5, read_integer_invalid
    li r3, 1
    add r1, r1, r3
    ld.bu r5, [r1]
    add r1, r1, r3
    calla basic_string_address
    ld.bu r0, [r6]
    jmpa read_len_close
read_len_literal:
    li r3, 1
    add r1, r1, r3
    ld.bu r0, [r1]
    add r1, r1, r3
    add r1, r1, r0
read_len_close:
    ld.bu r3, [r1]
    li r5, 0029h
    bne r3, r5, read_integer_invalid
    li r3, 1
    add r1, r1, r3
    ret
read_val_function:
    ld.bu r3, [r1]
    li r5, 0028h
    bne r3, r5, read_integer_invalid
    li r3, 1
    add r1, r1, r3
    ld.bu r3, [r1]
    li r5, 0083h
    beq r3, r5, read_val_literal
    li r5, 0084h
    bne r3, r5, read_integer_invalid
    li r3, 1
    add r1, r1, r3
    ld.bu r5, [r1]
    add r1, r1, r3
    calla basic_string_address
    ld.bu r5, [r6]
    add r6, r6, r3
    jmpa read_val_digits_start
read_val_literal:
    li r3, 1
    add r1, r1, r3
    ld.bu r5, [r1]
    add r1, r1, r3
    mov r6, r1
    add r1, r1, r5
read_val_digits_start:
    li r3, val_return_cursor
    st.w r1, [r3]
    li r0, 0
    li r7, 0
    ld.bu r3, [r6]
    li r1, 002dh
    bne r3, r1, read_val_digits
    li r7, 1
    li r3, 1
    add r6, r6, r3
    sub r5, r5, r3
read_val_digits:
    li r3, 0
    beq r5, r3, read_val_close
    ld.bu r3, [r6]
    li r1, 0030h
    blo r3, r1, read_val_close
    li r1, 003ah
    bhs r3, r1, read_val_close
    li r1, 10
    mul r0, r0, r1
    li r1, 0030h
    sub r3, r3, r1
    add r0, r0, r3
    li r3, 1
    add r6, r6, r3
    sub r5, r5, r3
    jmpa read_val_digits
read_val_close:
    li r3, 0
    beq r7, r3, read_val_expect_close
    neg r0, r0
read_val_expect_close:
    li r1, val_return_cursor
    ld.w r1, [r1]
    ld.bu r3, [r1]
    li r5, 0029h
    bne r3, r5, read_integer_invalid
    li r3, 1
    add r1, r1, r3
    ret
read_inp_function:
    calla read_function_argument
    mov r5, r0
    li r6, dynamic_in
    st.w r5, [r6 + 2]
dynamic_in:
    in r0, 0000h
    ret
read_point_function:
    calla parse_graphics_xy
    li r3, 1
    bne r0, r3, read_integer_invalid
    calla graphics_get_pixel
    ret
read_function_argument:
    ld.bu r3, [r1]
    li r5, 0028h
    bne r3, r5, read_integer_invalid
    li r3, 1
    add r1, r1, r3
    calla read_integer
    ld.bu r3, [r1]
    li r5, 0029h
    bne r3, r5, read_integer_invalid
    li r3, 1
    add r1, r1, r3
    ret
read_integer_invalid:
    li r0, 0
    ret

; BASIC 1.1 device and graphics statements. Coordinates outside the selected
; mode are clipped. Mode 2 colors are packed RGBA4444 values.
run_out:
    calla read_integer
    li r3, io_port
    st.w r0, [r3]
    ld.bu r3, [r1]
    li r5, 002ch
    bne r3, r5, run_syntax
    li r3, 1
    add r1, r1, r3
    calla read_integer
    li r3, io_port
    ld.w r5, [r3]
    li r6, dynamic_out
    st.w r5, [r6 + 2]
dynamic_out:
    out 0000h, r0
    jmpa run_token

run_screen:
    calla read_integer
    li r3, 3
    bhs r0, r3, run_syntax
    li r3, graphics_mode
    st.b r0, [r3]
    calla graphics_clear
    jmpa run_token
run_present:
    calla present_current_mode
    jmpa run_token
present_current_mode:
    li r3, graphics_mode
    ld.bu r0, [r3]
    out 0020h, r0
    ret
run_palette:
    calla read_integer
    out 0022h, r0
    li r7, 3
run_palette_component:
    ld.bu r3, [r1]
    li r5, 002ch
    bne r3, r5, run_syntax
    li r3, 1
    add r1, r1, r3
    push r7
    calla read_integer
    pop r7
    out 0023h, r0
    li r3, 1
    sub r7, r7, r3
    li r3, 0
    bne r7, r3, run_palette_component
    jmpa run_token

run_pset:
    calla parse_graphics_xy
    li r3, 1
    bne r0, r3, run_syntax
    ld.bu r3, [r1]
    li r5, 002ch
    bne r3, r5, run_syntax
    li r3, 1
    add r1, r1, r3
    calla read_integer
    li r3, graphics_color
    st.w r0, [r3]
    calla graphics_set_pixel
    jmpa run_token
run_preset:
    calla parse_graphics_xy
    li r3, 1
    bne r0, r3, run_syntax
    li r0, 0
    ld.bu r3, [r1]
    li r5, 002ch
    bne r3, r5, run_preset_color_ready
    li r3, 1
    add r1, r1, r3
    calla read_integer
run_preset_color_ready:
    li r3, graphics_color
    st.w r0, [r3]
    calla graphics_set_pixel
    jmpa run_token

; Parses (x,y), stores both words, and returns R0=1 on success.
parse_graphics_xy:
    ld.bu r3, [r1]
    li r5, 0028h
    bne r3, r5, parse_graphics_xy_fail
    li r3, 1
    add r1, r1, r3
    calla read_integer
    li r3, graphics_x
    st.w r0, [r3]
    ld.bu r3, [r1]
    li r5, 002ch
    bne r3, r5, parse_graphics_xy_fail
    li r3, 1
    add r1, r1, r3
    calla read_integer
    li r3, graphics_y
    st.w r0, [r3]
    ld.bu r3, [r1]
    li r5, 0029h
    bne r3, r5, parse_graphics_xy_fail
    li r3, 1
    add r1, r1, r3
    li r0, 1
    ret
parse_graphics_xy_fail:
    li r0, 0
    ret

run_line_graphics:
    calla parse_graphics_xy
    li r3, 1
    bne r0, r3, run_syntax
    li r3, graphics_x
    ld.w r0, [r3]
    li r5, line_x
    st.w r0, [r5]
    li r3, graphics_y
    ld.w r0, [r3]
    li r5, line_y
    st.w r0, [r5]
    ld.bu r3, [r1]
    li r5, 002dh
    bne r3, r5, run_syntax
    li r3, 1
    add r1, r1, r3
    calla parse_graphics_xy
    li r3, 1
    bne r0, r3, run_syntax
    li r3, graphics_x
    ld.w r0, [r3]
    li r5, line_x2
    st.w r0, [r5]
    li r3, graphics_y
    ld.w r0, [r3]
    li r5, line_y2
    st.w r0, [r5]
    ld.bu r3, [r1]
    li r5, 002ch
    bne r3, r5, run_syntax
    li r3, 1
    add r1, r1, r3
    calla read_integer
    li r3, graphics_color
    st.w r0, [r3]
    calla graphics_line_prepare
graphics_line_loop:
    li r3, line_x
    ld.w r0, [r3]
    li r5, graphics_x
    st.w r0, [r5]
    li r3, line_y
    ld.w r0, [r3]
    li r5, graphics_y
    st.w r0, [r5]
    calla graphics_set_pixel
    li r3, line_x
    ld.w r5, [r3]
    li r3, line_x2
    ld.w r6, [r3]
    bne r5, r6, graphics_line_step
    li r3, line_y
    ld.w r5, [r3]
    li r3, line_y2
    ld.w r6, [r3]
    beq r5, r6, graphics_line_done
graphics_line_step:
    li r3, line_error
    ld.w r5, [r3]
    add r7, r5, r5
    li r3, line_dy
    ld.w r6, [r3]
    blt r7, r6, graphics_line_y_step
    add r5, r5, r6
    li r3, line_error
    st.w r5, [r3]
    li r3, line_x
    ld.w r5, [r3]
    li r3, line_sx
    ld.w r6, [r3]
    add r5, r5, r6
    li r3, line_x
    st.w r5, [r3]
graphics_line_y_step:
    li r3, line_dx
    ld.w r6, [r3]
    bgt r7, r6, graphics_line_loop
    li r3, line_error
    ld.w r5, [r3]
    add r5, r5, r6
    st.w r5, [r3]
    li r3, line_y
    ld.w r5, [r3]
    li r3, line_sy
    ld.w r6, [r3]
    add r5, r5, r6
    li r3, line_y
    st.w r5, [r3]
    jmpa graphics_line_loop
graphics_line_done:
    jmpa run_token

graphics_line_prepare:
    li r3, line_x2
    ld.w r6, [r3]
    li r3, line_x
    ld.w r5, [r3]
    sub r0, r6, r5
    li r7, 1
    bge r0, r7, graphics_line_sx_ready
    li r7, -1
    neg r0, r0
graphics_line_sx_ready:
    li r3, line_sx
    st.w r7, [r3]
    li r3, line_dx
    st.w r0, [r3]
    li r3, line_y2
    ld.w r6, [r3]
    li r3, line_y
    ld.w r5, [r3]
    sub r0, r6, r5
    li r7, 1
    bge r0, r7, graphics_line_sy_ready
    li r7, -1
    neg r0, r0
graphics_line_sy_ready:
    li r3, line_sy
    st.w r7, [r3]
    neg r0, r0
    li r3, line_dy
    st.w r0, [r3]
    li r3, line_dx
    ld.w r5, [r3]
    add r0, r0, r5
    li r3, line_error
    st.w r0, [r3]
    ret

run_circle:
    calla parse_graphics_xy
    li r3, 1
    bne r0, r3, run_syntax
    li r3, graphics_x
    ld.w r0, [r3]
    li r5, circle_cx
    st.w r0, [r5]
    li r3, graphics_y
    ld.w r0, [r3]
    li r5, circle_cy
    st.w r0, [r5]
    ld.bu r3, [r1]
    li r5, 002ch
    bne r3, r5, run_syntax
    li r3, 1
    add r1, r1, r3
    calla read_integer
    li r3, 0
    blt r0, r3, run_syntax
    li r3, circle_x
    st.w r0, [r3]
    li r5, 1
    sub r0, r5, r0
    li r3, circle_error
    st.w r0, [r3]
    li r3, circle_y
    li r0, 0
    st.w r0, [r3]
    ld.bu r3, [r1]
    li r5, 002ch
    bne r3, r5, run_syntax
    li r3, 1
    add r1, r1, r3
    calla read_integer
    li r3, graphics_color
    st.w r0, [r3]
graphics_circle_loop:
    li r3, circle_x
    ld.w r5, [r3]
    li r3, circle_y
    ld.w r6, [r3]
    blt r5, r6, graphics_circle_done
    li r3, circle_plot_dx
    st.w r5, [r3]
    li r3, circle_plot_dy
    st.w r6, [r3]
    calla graphics_circle_plot_four
    li r3, circle_x
    ld.w r5, [r3]
    li r3, circle_plot_dy
    st.w r5, [r3]
    li r3, circle_y
    ld.w r6, [r3]
    li r3, circle_plot_dx
    st.w r6, [r3]
    calla graphics_circle_plot_four
    li r3, circle_y
    ld.w r5, [r3]
    li r6, 1
    add r5, r5, r6
    st.w r5, [r3]
    li r3, circle_error
    ld.w r5, [r3]
    li r6, 0
    blt r5, r6, graphics_circle_error_negative
    li r3, circle_x
    ld.w r6, [r3]
    li r7, 1
    sub r6, r6, r7
    st.w r6, [r3]
    li r3, circle_y
    ld.w r7, [r3]
    sub r7, r7, r6
    add r7, r7, r7
    li r6, 1
    add r7, r7, r6
    add r7, r7, r5
    li r3, circle_error
    st.w r7, [r3]
    jmpa graphics_circle_loop
graphics_circle_error_negative:
    li r3, circle_y
    ld.w r6, [r3]
    add r6, r6, r6
    li r7, 1
    add r6, r6, r7
    add r5, r5, r6
    li r3, circle_error
    st.w r5, [r3]
    jmpa graphics_circle_loop
graphics_circle_done:
    jmpa run_token

; Draws the four sign combinations of circle_plot_dx/circle_plot_dy.
graphics_circle_plot_four:
    li r3, circle_cx
    ld.w r5, [r3]
    li r3, circle_plot_dx
    ld.w r6, [r3]
    add r0, r5, r6
    li r3, graphics_x
    st.w r0, [r3]
    li r3, circle_cy
    ld.w r5, [r3]
    li r3, circle_plot_dy
    ld.w r6, [r3]
    add r0, r5, r6
    li r3, graphics_y
    st.w r0, [r3]
    calla graphics_set_pixel
    li r3, circle_cx
    ld.w r5, [r3]
    li r3, circle_plot_dx
    ld.w r6, [r3]
    sub r0, r5, r6
    li r3, graphics_x
    st.w r0, [r3]
    calla graphics_set_pixel
    li r3, circle_cy
    ld.w r5, [r3]
    li r3, circle_plot_dy
    ld.w r6, [r3]
    sub r0, r5, r6
    li r3, graphics_y
    st.w r0, [r3]
    calla graphics_set_pixel
    li r3, circle_cx
    ld.w r5, [r3]
    li r3, circle_plot_dx
    ld.w r6, [r3]
    add r0, r5, r6
    li r3, graphics_x
    st.w r0, [r3]
    calla graphics_set_pixel
    ret

graphics_clear:
    push r1
    push r2
    push r4
    rdsg r7
    push r7
    li r5, 003dh
graphics_clear_page:
    wrsg r5
    li r6, 0c000h
    li r7, 04000h
    li r0, 0
graphics_clear_byte:
    st.b r0, [r6]
    li r3, 1
    add r6, r6, r3
    sub r7, r7, r3
    li r3, 0
    bne r7, r3, graphics_clear_byte
    li r3, 1
    add r5, r5, r3
    li r3, 0040h
    bne r5, r3, graphics_clear_page
    pop r7
    wrsg r7
    pop r4
    pop r2
    pop r1
    ret

; Maps a 0..BFFFh VRAM byte offset into SG and returns logical address in R6.
graphics_map_offset:
    li r7, 14
    shr r5, r0, r7
    li r7, 003dh
    add r5, r5, r7
    wrsg r5
    li r6, 03fffh
    and r6, r0, r6
    li r7, 0c000h
    or r6, r6, r7
    ret

; Resolves graphics_x/graphics_y in the selected mode. R0=1 and R6 is the
; mapped logical byte address on success; R0=0 clips an invalid coordinate.
graphics_locate_pixel:
    li r3, graphics_mode
    ld.bu r3, [r3]
    li r5, 0
    beq r3, r5, graphics_locate_mode0
    li r5, 1
    beq r3, r5, graphics_locate_mode1
    li r3, graphics_x
    ld.w r5, [r3]
    li r6, 128
    bhs r5, r6, graphics_locate_invalid
    li r3, graphics_y
    ld.w r0, [r3]
    li r6, 96
    bhs r0, r6, graphics_locate_invalid
    li r6, 9
    shl r0, r0, r6
    li r6, 2
    shl r5, r5, r6
    add r0, r0, r5
    jmpa graphics_locate_map
graphics_locate_mode0:
    li r3, graphics_x
    ld.w r5, [r3]
    li r6, 256
    bhs r5, r6, graphics_locate_invalid
    li r3, graphics_y
    ld.w r0, [r3]
    li r6, 192
    bhs r0, r6, graphics_locate_invalid
    li r6, 8
    shl r0, r0, r6
    add r0, r0, r5
    jmpa graphics_locate_map
graphics_locate_mode1:
    li r3, graphics_x
    ld.w r5, [r3]
    li r6, 512
    bhs r5, r6, graphics_locate_invalid
    li r3, graphics_y
    ld.w r0, [r3]
    li r6, 384
    bhs r0, r6, graphics_locate_invalid
    li r6, 7
    shl r0, r0, r6
    li r6, 2
    shr r7, r5, r6
    add r0, r0, r7
graphics_locate_map:
    calla graphics_map_offset
    li r0, 1
    ret
graphics_locate_invalid:
    li r0, 0
    ret

graphics_set_pixel:
    push r1
    push r2
    push r4
    rdsg r7
    push r7
    calla graphics_locate_pixel
    li r3, 0
    beq r0, r3, graphics_set_done
    li r3, graphics_mode
    ld.bu r3, [r3]
    li r5, 0
    beq r3, r5, graphics_set_mode0
    li r5, 1
    beq r3, r5, graphics_set_mode1
    li r3, graphics_color
    ld.w r5, [r3]
    li r7, 12
    shr r0, r5, r7
    li r7, 17
    mul r0, r0, r7
    st.b r0, [r6]
    li r7, 8
    shr r0, r5, r7
    li r7, 000fh
    and r0, r0, r7
    li r7, 17
    mul r0, r0, r7
    st.b r0, [r6 + 1]
    li r7, 4
    shr r0, r5, r7
    li r7, 000fh
    and r0, r0, r7
    li r7, 17
    mul r0, r0, r7
    st.b r0, [r6 + 2]
    li r7, 000fh
    and r0, r5, r7
    li r7, 17
    mul r0, r0, r7
    st.b r0, [r6 + 3]
    jmpa graphics_set_done
graphics_set_mode0:
    li r3, graphics_color
    ld.w r5, [r3]
    st.b r5, [r6]
    jmpa graphics_set_done
graphics_set_mode1:
    ld.bu r0, [r6]
    li r3, graphics_x
    ld.w r5, [r3]
    li r7, 3
    and r5, r5, r7
    li r7, 3
    sub r5, r7, r5
    li r7, 1
    shl r5, r5, r7
    li r7, 3
    shl r7, r7, r5
    not r7, r7
    and r0, r0, r7
    li r3, graphics_color
    ld.w r7, [r3]
    li r3, 3
    and r7, r7, r3
    shl r7, r7, r5
    or r0, r0, r7
    st.b r0, [r6]
graphics_set_done:
    pop r7
    wrsg r7
    pop r4
    pop r2
    pop r1
    ret

graphics_get_pixel:
    push r1
    push r2
    push r4
    rdsg r7
    push r7
    calla graphics_locate_pixel
    li r3, 0
    beq r0, r3, graphics_get_zero
    li r3, graphics_mode
    ld.bu r3, [r3]
    li r5, 0
    beq r3, r5, graphics_get_mode0
    li r5, 1
    beq r3, r5, graphics_get_mode1
    ld.bu r5, [r6]
    li r7, 00f0h
    and r5, r5, r7
    li r7, 8
    shl r5, r5, r7
    ld.bu r0, [r6 + 1]
    li r7, 00f0h
    and r0, r0, r7
    li r7, 4
    shl r0, r0, r7
    or r5, r5, r0
    ld.bu r0, [r6 + 2]
    li r7, 00f0h
    and r0, r0, r7
    or r5, r5, r0
    ld.bu r0, [r6 + 3]
    li r7, 4
    shr r0, r0, r7
    or r0, r5, r0
    jmpa graphics_get_done
graphics_get_mode0:
    ld.bu r0, [r6]
    jmpa graphics_get_done
graphics_get_mode1:
    ld.bu r0, [r6]
    li r3, graphics_x
    ld.w r5, [r3]
    li r7, 3
    and r5, r5, r7
    li r7, 3
    sub r5, r7, r5
    li r7, 1
    shl r5, r5, r7
    shr r0, r0, r5
    li r7, 3
    and r0, r0, r7
    jmpa graphics_get_done
graphics_get_zero:
    li r0, 0
graphics_get_done:
    pop r7
    wrsg r7
    pop r4
    pop r2
    pop r1
    ret

run_cls:
    li r0, 0
    out 0037h, r0
    jmpa run_token
run_color:
    calla read_integer
    out 0032h, r0
    ld.bu r3, [r1]
    li r5, 002ch
    bne r3, r5, run_token
    li r3, 1
    add r1, r1, r3
    calla read_integer
    out 0033h, r0
    jmpa run_token
run_locate:
    calla read_integer
    out 0031h, r0
    ld.bu r3, [r1]
    li r5, 002ch
    bne r3, r5, run_syntax
    li r3, 1
    add r1, r1, r3
    calla read_integer
    out 0030h, r0
    jmpa run_token
run_dim:
    ld.bu r3, [r1]
    li r5, 0084h
    bne r3, r5, run_syntax
    li r3, 2
    add r1, r1, r3
    ld.bu r3, [r1]
    li r5, 0028h
    bne r3, r5, run_syntax
    li r3, 1
    add r1, r1, r3
    calla read_integer
    li r3, 16
    bhs r0, r3, run_syntax
    ld.bu r3, [r1]
    li r5, 0029h
    bne r3, r5, run_syntax
    li r3, 1
    add r1, r1, r3
    jmpa run_token
run_rem:
    mov r1, r4
    jmpa run_line
run_syntax:
    li r1, syntax_text
    calla puts
    ret
run_done:
    calla present_current_mode
    ret
run_stop:
    li r3, continuation_cursor
    st.w r1, [r3]
    li r3, continuation_program_end
    st.w r2, [r3]
    li r3, continuation_line_end
    st.w r4, [r3]
    li r3, continuation_valid
    li r5, 1
    st.b r5, [r3]
    li r1, break_text
    calla puts
    jmpa run_done
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
    li r2, 000ah
    bne r0, r2, puts_character
    calla newline
    jmpa puts
puts_character:
    calla putc
    jmpa puts
puts_done:
    ret

newline:
    li r0, 000dh
    calla putc
    li r0, 000ah
    calla putc
    ret

keyboard_interrupt:
    push r0
    push r1
    push r2
    push r3
    push r4
    push r5
    push r6
    push r7
    li r0, 0210h
    ld.bu r1, [r0]
    ld.bu r2, [r0 + 1]
    ld.bu r3, [r0 + 2]
    li r5, 0
    li r6, 0
    li r7, 2
    shr r4, r1, r7
    calla keyboard_check_ctrl_c
    li r7, 0003h
    and r4, r1, r7
    li r7, 4
    shl r4, r4, r7
    li r7, 4
    shr r0, r2, r7
    or r4, r4, r0
    calla keyboard_check_ctrl_c
    li r7, 000fh
    and r4, r2, r7
    li r7, 2
    shl r4, r4, r7
    li r7, 6
    shr r0, r3, r7
    or r4, r4, r0
    calla keyboard_check_ctrl_c
    li r7, 003fh
    and r4, r3, r7
    calla keyboard_check_ctrl_c
    li r0, 1
    bne r5, r0, keyboard_interrupt_done
    bne r6, r0, keyboard_interrupt_done
    li r0, break_requested
    li r1, 1
    st.b r1, [r0]
keyboard_interrupt_done:
    pop r7
    pop r6
    pop r5
    pop r4
    pop r3
    pop r2
    pop r1
    pop r0
    iret

; R4 is one decoded scan-code. R5/R6 track Ctrl/C respectively.
keyboard_check_ctrl_c:
    li r0, 0039h
    bne r4, r0, keyboard_check_c
    li r5, 1
keyboard_check_c:
    li r0, 0030h
    bne r4, r0, keyboard_check_done
    li r6, 1
keyboard_check_done:
    ret

banner_text:
    .byte 'O','P','E','N','1','6','A',' ','B','A','S','I','C',' ','1','.','1',10,0
ready_text:
    .byte 'R','E','A','D','Y','.',10,0
syntax_text:
    .byte '?','S','Y','N','T','A','X',' ','E','R','R','O','R',10,0
break_text:
    .byte 'B','R','E','A','K',10,0
input_prompt_text:
    .byte '?',' ',0
no_program_text:
    .byte '?','N','O',' ','P','R','O','G','R','A','M',10,0
program_full_text:
    .byte '?','P','R','O','G','R','A','M',' ','F','U','L','L',10,0
record_token_length:
    .word 0
record_string_source:
    .word 0
insert_position:
    .word 0
break_requested:
    .byte 0
list_next:
    .word 0
list_end_address:
    .word 0
program_old_end:
    .word 0
input_length:
    .byte 0
stored_line_number:
    .word 0
token_cursor:
    .word 0
token_out:
    .word 0
token_count:
    .word 0
token_number:
    .word 0
token_word_start:
    .word 0
token_word_length:
    .word 0
token_variable_type:
    .byte 0
token_variable_value:
    .byte 0
assignment_variable:
    .byte 0
assignment_is_array:
    .byte 0
assignment_array_address:
    .word 0
array_variable:
    .byte 0
poke_address:
    .word 0
string_destination:
    .word 0
val_return_cursor:
    .word 0
if_left_value:
    .word 0
if_operator:
    .byte 0
if_second_operator:
    .byte 0
gosub_depth:
    .byte 0
for_depth:
    .byte 0
for_new_variable:
    .byte 0
for_active_variable:
    .byte 0
for_new_limit:
    .word 0
for_new_step:
    .word 1
input_mode:
    .byte 0
input_target_variable:
    .byte 0
saved_run_cursor:
    .word 0
saved_program_end:
    .word 0
saved_line_end:
    .word 0
read_target_variable:
    .byte 0
data_cursor:
    .word 0
data_line_end:
    .word 0
data_scan_pointer:
    .word 400ah
continuation_valid:
    .byte 0
continuation_cursor:
    .word 0
continuation_program_end:
    .word 0
continuation_line_end:
    .word 0
io_port:
    .word 0
graphics_mode:
    .byte 0
graphics_x:
    .word 0
graphics_y:
    .word 0
graphics_color:
    .word 0
line_x:
    .word 0
line_y:
    .word 0
line_x2:
    .word 0
line_y2:
    .word 0
line_dx:
    .word 0
line_dy:
    .word 0
line_sx:
    .word 0
line_sy:
    .word 0
line_error:
    .word 0
circle_cx:
    .word 0
circle_cy:
    .word 0
circle_x:
    .word 0
circle_y:
    .word 0
circle_error:
    .word 0
circle_plot_dx:
    .word 0
circle_plot_dy:
    .word 0
statement_dispatch_table:
    ; 90h-97h
    .word run_let,run_print,run_input,run_if,0,run_goto,run_gosub,run_return
    ; 98h-9Fh
    .word run_for,0,0,run_next,run_rem,run_done,run_stop,run_dim
    ; A0h-A7h
    .word run_cls,run_color,run_locate,0,0,0,0,0
    ; A8h-AFh
    .word 0,0,0,0,0,0,0,0
    ; B0h-B7h
    .word 0,0,0,0,run_poke,0,run_data,run_read
    ; B8h-BFh
    .word run_restore,0,run_screen,run_pset,run_preset,run_line_graphics,run_circle,run_palette
    ; C0h-C1h
    .word run_present,run_out
token_keyword_table:
    .byte 090h,3,'l','e','t', 091h,5,'p','r','i','n','t'
    .byte 092h,5,'i','n','p','u','t', 093h,2,'i','f'
    .byte 094h,4,'t','h','e','n', 095h,4,'g','o','t','o'
    .byte 096h,5,'g','o','s','u','b', 097h,6,'r','e','t','u','r','n'
    .byte 098h,3,'f','o','r', 099h,2,'t','o', 09ah,4,'s','t','e','p'
    .byte 09bh,4,'n','e','x','t', 09ch,3,'r','e','m', 09dh,3,'e','n','d'
    .byte 09eh,4,'s','t','o','p', 09fh,3,'d','i','m', 0a0h,3,'c','l','s'
    .byte 0a1h,5,'c','o','l','o','r', 0a2h,6,'l','o','c','a','t','e'
    .byte 0a3h,3,'a','b','s', 0a4h,3,'i','n','t', 0a5h,3,'s','g','n'
    .byte 0a6h,3,'l','e','n', 0ach,3,'v','a','l'
    .byte 0adh,3,'a','n','d', 0aeh,2,'o','r', 0afh,3,'n','o','t'
    .byte 0b0h,3,'r','u','n', 0b1h,4,'l','i','s','t', 0b2h,3,'n','e','w'
    .byte 0b3h,4,'p','e','e','k', 0b4h,4,'p','o','k','e', 0b5h,4,'e','l','s','e'
    .byte 0b6h,4,'d','a','t','a', 0b7h,4,'r','e','a','d'
    .byte 0b8h,7,'r','e','s','t','o','r','e', 0b9h,4,'c','o','n','t'
    .byte 0bah,6,'s','c','r','e','e','n', 0bbh,4,'p','s','e','t'
    .byte 0bch,6,'p','r','e','s','e','t', 0bdh,4,'l','i','n','e'
    .byte 0beh,6,'c','i','r','c','l','e', 0bfh,7,'p','a','l','e','t','t','e'
    .byte 0c0h,7,'p','r','e','s','e','n','t', 0c1h,3,'o','u','t'
    .byte 0c2h,3,'i','n','p', 0c3h,5,'p','o','i','n','t', 0
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
