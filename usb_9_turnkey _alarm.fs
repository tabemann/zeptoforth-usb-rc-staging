\
\ Simple turnkey test only - not part of USB device stack
\ 

compile-to-flash

marker erase-turnkey-test

led import
task import
alarm import
systick import
usb-core import
variable countdown
variable turnkey-test-task

alarm-size aligned-buffer: turnkey-led-alarm

: turnkey-wait-for-client-connect ( -- )

  usb-device-configured? @ if  \ USB console available - flash green led until client connects

    0 [: begin green toggle-led 125 ms again ;] 256 128 512 spawn turnkey-test-task !
  
    turnkey-test-task @ run

    begin
      DTR? @ not if pause-wo-reschedule then
      DTR? @ if true else false then
    until

    100000. timer::delay-us \ allow client time to settle after connection

    turnkey-test-task @ kill 

  else

   \ no USB host - drop through to UART console

  then

  on green led!

;

: turnkey

  turnkey-wait-for-client-connect 

  false ack-nak-enabled !

  10 countdown !

  cr

  10 0 do 

    countdown @ (.) ." .." 50 ms countdown @ 1 - countdown ! 

  loop

  ." 0 Turnkey Reboot Complete. USB Address " usb-get-device-address . cr

  30000 1 0 [: off green led! ;] turnkey-led-alarm set-alarm-delay-default
;