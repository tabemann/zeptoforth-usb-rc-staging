\ Copyright (c) 2023-2025 Travis Bemann
\ Copyright (c) 2025 Serialcomms (GitHub)
\
\ Permission is hereby granted, free of charge, to any person obtaining a copy
\ of this software and associated documentation files (the "Software"), to deal
\ in the Software without restriction, including without limitation the rights
\ to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
\ copies of the Software, and to permit persons to whom the Software is
\ furnished to do so, subject to the following conditions:
\
\ The above copyright notice and this permission notice shall be included in
\ all copies or substantial portions of the Software.
\
\ THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
\ IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY
\ FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
\ AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
\ LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
\ OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
\ SOFTWARE.

\ IMPORTANT - many terminal clients use the same control keys as zeptoforth.
\ Use false usb::usb-special-enabled? ! to allow full use of these clients.
\ Note also that some clients may hang if Pico reboots and does not recover.

\ minicom -F " Zeptoforth | %D | %b | %t | Minicom %V | %T | %H Help "  -z zeptoforth  -D /dev/ttyACM1
\             <------------ minicom status bar display ------------->  <settings file>       

compile-to-flash

continue-module usb

  console import
  watchdog import
  
  begin-module usb-internal

    task import
    slock import
    systick import
    usb-core import
    usb-constants import
    usb-cdc-buffers import

    \ Receive buffer simple lock
    slock-size buffer: usb-key-lock
    
    \ Transmit buffer simple lock
    slock-size buffer: usb-emit-lock

    : start-ep1-data-transfer-to-host ( -- )
      tx-empty? not if
        ep1-start-ring-transfer-to-host
      then
    ;

    : start-ep1-data-transfer-to-pico ( -- )
      rx-free 63 > if
        EP1-to-Pico 64 usb-receive-data-packet
      then
    ;

    \ USB Start of Frame Interrupts, every 1 ms
    : handle-sof-from-host ( -- )
      update-watchdog \ update watchdog to prevent reboot (USB core is running)
      EP1-to-Pico endpoint-busy? @ not if start-ep1-data-transfer-to-pico then
      EP1-to-Host endpoint-busy? @ not if start-ep1-data-transfer-to-host then
    ;

    \ Byte available to read from rx ring buffer ?
    : usb-key? ( -- key? )
      rx-empty? not
    ;

    \ USB host and client connected and tx ring buffer capacity to host available ?
    : usb-emit? ( -- emit? )
      usb-device-configured? @ usb-dtr? and tx-full? not and  
    ;

    : usb-emit ( c -- )
      begin
        [:          
          usb-emit? dup if swap write-tx then
        ;] usb-emit-lock with-slock
        dup not if pause-reschedule-last then
      until
    ;

    : usb-key ( -- c)
      begin
        [:
          usb-key? dup if read-rx swap then
        ;] usb-key-lock with-slock
        dup not if pause-reschedule-last then
      until
    ;

    : usb-wait-for-device-connected ( -- )
      systick-counter 200000 + { systick-end } \ 20 seconds max wait if USB wall/battery power only
      begin
        usb-device-connected? @ not if pause-wo-reschedule then
        usb-device-connected? @ systick-counter systick-end > or 
      until
    ;
    
    : usb-wait-for-device-configured ( -- )     
      systick-counter 1800000 + { systick-end } \ up to 180 seconds if PC cold boot
      begin
        usb-device-configured? @ not if pause-wo-reschedule then
        usb-device-configured? @ systick-counter systick-end > or 
      until
    ;

    : usb-wait-for-client-connected ( -- )     \ optional blocking wait if required.
      begin
        DTR? @ not if pause-wo-reschedule then
        DTR? @ if true else false then
      until
    ;

    : usb-flush-console ( -- )
      systick-counter 30000 + { systick-end } \ 3.0 seconds
      begin
        tx-empty? not if pause-wo-reschedule then
        tx-empty? systick-counter systick-end > or 
      until
    ;

    \ Switch to USB console
    : switch-to-usb-console ( -- )

      ['] usb-key? key?-hook !
      ['] usb-key key-hook !
      ['] usb-emit? emit?-hook !
      ['] usb-emit emit-hook !

      ['] usb-emit? error-emit?-hook !
      ['] usb-emit error-emit-hook !
      ['] usb-flush-console flush-console-hook !
      ['] usb-flush-console error-flush-console-hook !
    ;

    : start-usb-console ( -- )
      ['] handle-sof-from-host sof-callback-handler !
      enable-watchdog \ to reboot if USB becomes wedged for any reason
      disable-multitasker-update-watchdog
      100000. timer::delay-us \ allow hosts and clients time to settle
      switch-to-usb-console 
      usb-set-modem-online
    ;

    \ Initialize USB console
    : init-usb-console ( -- )
      
      init-usb
      init-tx-ring
      init-rx-ring

      usb-key-lock init-slock
      usb-emit-lock init-slock

      usb-insert-device

      usb-wait-for-device-connected

      usb-device-connected? @ if       \ Pico connected to active USB host - not just USB powered
        
        usb-wait-for-device-configured \ must wait for host to set configuration - allow up to 180s for cold-boot host PC  
                                       
        usb-device-configured? @ if 

          start-usb-console 
      
        else

          \ Pico usb device configuration not set by host. 
          \ Possible host or device configuration issues.
          \ Remove device and fall through to UART serial
          \ and turnkey if configured.
        
        usb-remove-device

        then

      else

        \ No usb host, assume on wall or battery power. 
        \ Remove device and fall through to UART serial
        \ and turnkey if configured.

        usb-remove-device

      then
    ;

    initializer init-usb-console

  end-module> import

  \ Select the USB serial console
  : usb-console ( -- ) start-usb-console ;

  \ Set the curent input to usb within an xt
  : with-usb-input ( xt -- )
    ['] usb-key ['] usb-key? rot with-input
  ;

  \ Set the current output to usb within an xt
  : with-usb-output ( xt -- )
    ['] usb-emit ['] usb-emit? rot ['] usb-flush-console swap with-output
  ;

  \ Set the current error output to usb within an xt
  : with-usb-error-output ( xt -- )
    ['] usb-emit ['] usb-emit? rot ['] usb-flush-console swap with-error-output
  ;

end-module

compile-to-ram

\ USB_4_CDC_CONSOLE (JANUARY 2025) END ===============================================
