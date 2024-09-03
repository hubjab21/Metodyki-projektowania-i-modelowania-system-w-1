idf_component_register(SRCS "nimBLE.c"
                       INCLUDE_DIRS "include"
                       PRIV_REQUIRES "esp_event"
                                     "nvs_flash"
                                     "bt"
                                     "driver"
                                     "adc_sensor")
 