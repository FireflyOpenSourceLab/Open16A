; Open16A Embedded ASM coprocessor firmware - command server.
;
; Layout (flat 64 KiB, logical == physical):
;   IVT[0]   0010h    external command vector
;   entry    0300h
;   mailbox  FC00h-FFFFh
;
; On startup the firmware installs the external command handler at vector 0,
; enables interrupts and HALT-waits. Each external command arrives with the
; 16-bit command word in R0 and this slot's 1 KiB mailbox snapshot at FC00h.
; The handler echoes the command word and an ACK status into the mailbox, then
; IRETs back to the HALT wait; the host card then writes the mailbox back.

main:
    li   r1, handler
    li   r2, 0010h            ; interrupt vector 0 entry (external commands)
    st.w r1, [r2]
    ei

wait:
    halt
    jmpa wait                 ; after IRET, return to the HALT wait

handler:
    li   r1, 0FC00h           ; internal mailbox base
    st.w r0, [r1]             ; echo command word (big-endian) at mailbox+0
    li   r2, 1
    st.w r2, [r1 + 2]         ; status word = 0001h (ACK) at mailbox+2
    iret
