main:
    di
    li   r0, video_interrupt
    li   r1, 0030h
    st.w r0, [r1]
    li   r0, keyboard_interrupt
    li   r1, 0032h
    st.w r0, [r1]
    ei

    calla clear_vram

    li   r0, 0
    li   r1, 0
    li   r2, 255
    li   r3, 0

main_loop:
    calla draw_pixels
    calla consume_key_event
    jmpa main_loop

stop_program:
    calla flush_draw
    jmpa stop_program

consume_key_event:
    push r5
    push r6
    push r7

    li   r7, key_flags
    ld.w r6, [r7]
    li   r5, 0
    st.w r5, [r7]

    li   r5, 3
    beq  r6, r5, stop_program
    li   r5, 12
    beq  r6, r5, stop_program

    li   r5, 1
    beq  r6, r5, move_up
    li   r5, 2
    beq  r6, r5, move_right
    li   r5, 4
    beq  r6, r5, move_down
    li   r5, 8
    beq  r6, r5, move_left
    jmpa consume_key_event_done

move_up:
    li   r5, 1
    sub  r1, r1, r5
    jmpa consume_key_event_done

move_right:
    li   r5, 1
    add  r0, r0, r5
    jmpa consume_key_event_done

move_down:
    li   r5, 1
    add  r1, r1, r5
    jmpa consume_key_event_done

move_left:
    li   r5, 1
    sub  r0, r0, r5

consume_key_event_done:
    pop  r7
    pop  r6
    pop  r5
    ret

pixel_offset_x:
    .word 0, 1, 0, -1
pixel_offset_y:
    .word -1, 0, 1, 0

draw_pixels:
    push r0
    push r1
    push r2
    push r3
    push r4
    push r5
    push r6
    push r7

    mov  r5, r0
    mov  r6, r1
    li   r2, 0
    li   r3, 4

draw_pixels_loop:
    beq  r2, r3, draw_pixels_done

    li    r7, 2
    mul   r7, r2, r7
    ld.w  r4, [r7 + pixel_offset_x]
    add   r0, r5, r4
    ld.w  r4, [r7 + pixel_offset_y]
    add   r1, r6, r4

    li    r7, 256
    mul   r7, r0, r7
    add   r7, r7, r1

    li    r4, 4000h
    blt   r7, r4, draw_segment_1
    sub   r7, r7, r4
    blt   r7, r4, draw_segment_2
    sub   r7, r7, r4
    blt   r7, r4, draw_segment_3
    jmpa  draw_pixels_next

draw_segment_1:
    rdsg  r4
    wsgi  003Dh
    calla write_pixel
    wrsg  r4
    jmpa  draw_pixels_next

draw_segment_2:
    rdsg  r4
    wsgi  003Eh
    calla write_pixel
    wrsg  r4
    jmpa  draw_pixels_next

draw_segment_3:
    rdsg  r4
    wsgi  003Fh
    calla write_pixel
    wrsg  r4

draw_pixels_next:
    li    r7, 1
    add   r2, r2, r7
    jmpa  draw_pixels_loop

draw_pixels_done:
    calla flush_draw
    pop   r7
    pop   r6
    pop   r5
    pop   r4
    pop   r3
    pop   r2
    pop   r1
    pop   r0
    ret

write_pixel:
    push r4
    push r7
    li   r4, 0C000h
    add  r7, r4, r7
    li   r4, 255
    st.b r4, [r7]
    pop  r7
    pop  r4
    ret

clear_vram:
    push r0
    push r1
    push r2
    push r3
    push r4
    push r5

    rdsg r2
    wsgi 003Dh
    li   r0, 0C000h
    li   r1, 0
    li   r3, 4000h
    li   r4, 0

clear_vram_page_1:
    beq  r4, r3, clear_vram_page_2_start
    add  r5, r0, r4
    st.w r1, [r5]
    li   r5, 2
    add  r4, r4, r5
    jmpa clear_vram_page_1

clear_vram_page_2_start:
    wsgi 003Eh
    li   r0, 0C000h
    li   r4, 0

clear_vram_page_2:
    beq  r4, r3, clear_vram_page_3_start
    add  r5, r0, r4
    st.w r1, [r5]
    li   r5, 2
    add  r4, r4, r5
    jmpa clear_vram_page_2

clear_vram_page_3_start:
    wsgi 003Fh
    li   r0, 0C000h
    li   r4, 0

clear_vram_page_3:
    beq  r4, r3, clear_vram_done
    add  r5, r0, r4
    st.w r1, [r5]
    li   r5, 2
    add  r4, r4, r5
    jmpa clear_vram_page_3

clear_vram_done:
    calla flush_draw
    wrsg r2
    pop  r5
    pop  r4
    pop  r3
    pop  r2
    pop  r1
    pop  r0
    ret

flush_draw:
    push r0
    li   r0, 0
    out  0020h, r0
    halt
    pop  r0
    ret

video_interrupt:
    iret

keyboard_interrupt:
    push r0
    push r1
    push r2
    push r3
    push r4
    push r5
    push r6
    push r7

    li   r0, 0
    li   r3, 0210h
    ld.w r1, [r3]

    li   r3, 10
    shr  r4, r1, r3
    li   r3, 3
    and  r2, r1, r3
    li   r3, 4
    shl  r2, r2, r3
    shr  r1, r1, r3
    li   r3, 15
    and  r1, r1, r3
    or   r2, r2, r1

    li   r5, 0
    li   r6, 030h
    bne  r4, r6, keyboard_check_key2_c
    li   r7, 2
    or   r5, r5, r7

keyboard_check_key2_c:
    bne  r2, r6, keyboard_check_ctrl
    li   r7, 1
    or   r5, r5, r7

keyboard_check_ctrl:
    li   r6, 039h
    bne  r4, r6, keyboard_check_key2_ctrl
    li   r7, 1
    or   r5, r5, r7

keyboard_check_key2_ctrl:
    bne  r2, r6, keyboard_store_flags
    li   r7, 4
    or   r5, r5, r7

keyboard_store_flags:
    li   r7, key_flags
    st.w r5, [r7]
    pop  r7
    pop  r6
    pop  r5
    pop  r4
    pop  r3
    pop  r2
    pop  r1
    pop  r0
    iret

key_flags:
    .word 0
