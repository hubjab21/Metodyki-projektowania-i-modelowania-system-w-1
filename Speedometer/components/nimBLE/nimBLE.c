#include <stdio.h>
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "freertos/event_groups.h"
#include "esp_event.h"
#include "nvs_flash.h"
#include "esp_log.h"
#include "esp_nimble_hci.h"
#include "nimble/nimble_port.h"
#include "nimble/nimble_port_freertos.h"
#include "host/ble_hs.h"
#include "services/gap/ble_svc_gap.h"
#include "services/gatt/ble_svc_gatt.h"
#include "sdkconfig.h"
#include "nimBLE.h"
#include "driver/gpio.h"
#include "driver/timer.h"
#include "adc_sensor.h"

#define TIMER_DIVIDER  80  // Hardware timer clock divider (1 µs per tick)

char *TAG = "BLE_Speedometer";
uint8_t ble_addr_type; 
struct ble_gap_adv_params adv_params;
bool status = false; 
uint64_t start_time = 0; 
uint64_t stop_time = 0; 
uint64_t time_diffrence = 0;
bool calibration_flag = false;

void init_timer(timer_group_t group, timer_idx_t timer) {
    timer_config_t config = {
        .divider = TIMER_DIVIDER,
        .counter_dir = TIMER_COUNT_UP,
        .counter_en = TIMER_START,
        .alarm_en = TIMER_ALARM_DIS,
        .auto_reload = false,
    };
    timer_init(group, timer, &config);
    timer_set_counter_value(group, timer, 0x00000000ULL);
    timer_start(group, timer);
}

static int device_write(uint16_t conn_handle, uint16_t attr_handle, 
                        struct ble_gatt_access_ctxt *ctxt, void *arg){
    char data[ctxt->om->om_len + 1];
    memcpy(data, ctxt->om->om_data, ctxt->om->om_len);
    data[ctxt->om->om_len] = '\0';

    if (strcmp(data, "START") == 0) {
        ESP_LOGI(TAG, "START\n");
        init_timer(TIMER_GROUP_0, TIMER_1);
        stop_time = 0;
        timer_get_counter_value(TIMER_GROUP_0, TIMER_1, &start_time);
        start_time /= 1000; // Convert to milliseconds
        calibration_flag = true;


    } else if (strcmp(data, "STOP") == 0) {
        timer_get_counter_value(TIMER_GROUP_0, TIMER_1, &stop_time);
        stop_time /= 1000;
        time_diffrence = stop_time - start_time;
        start_time = 0;
        ESP_LOGI(TAG, "STOP\n %llu", time_diffrence);
        timer_pause(TIMER_GROUP_0, TIMER_1);
        calibration_flag = false;
    } else {
        printf("Data from the client: %.*s\n", ctxt->om->om_len, ctxt->om->om_data);
    }
    return 0; 
}

static int device_read (uint16_t conn_handle, uint16_t attr_handle, 
                        struct ble_gatt_access_ctxt *ctxt, void *arg){
    uint32_t *data = (uint32_t*)arg;

    os_mbuf_append(ctxt->om, (void*)data, sizeof(uint32_t));
    return 0; 
}

static int device_read_speed (uint16_t conn_handle, uint16_t attr_handle, 
                        struct ble_gatt_access_ctxt *ctxt, void *arg){
    static char speed_str[16]; // Buffer to hold the speed string
    if(diameter != 0){
        snprintf(speed_str, sizeof(speed_str), "%.2f", speed);
    }
    else{
        snprintf(speed_str, sizeof(speed_str), "Calibrate");
    }
    os_mbuf_append(ctxt->om, speed_str, strlen(speed_str));
    return 0;
}

struct ble_gatt_chr_def gatt_char_defs[] = {
    {.uuid = BLE_UUID16_DECLARE(0xFEF4),
     .flags = BLE_GATT_CHR_F_READ,
     .access_cb = device_read,
     .arg = &sensor_data
    },
    {
     .uuid = BLE_UUID16_DECLARE(0xFEF5),
     .flags = BLE_GATT_CHR_F_READ,
     .access_cb = device_read_speed,
     .arg = NULL
    },
    {.uuid = BLE_UUID16_DECLARE(0xDEAD),
     .flags = BLE_GATT_CHR_F_WRITE,
     .access_cb = device_write
    },
    {0}
};

const struct ble_gatt_svc_def gatt_cfg[] = {
    {.type = BLE_GATT_SVC_TYPE_PRIMARY,
     .uuid = BLE_UUID16_DECLARE(0x1234),
     .characteristics = gatt_char_defs
    },
    {0}
};

// BLE event handling
int ble_gap_event(struct ble_gap_event *event, void *arg)
{
    switch (event->type)
    {
    // Advertise if connected
    case BLE_GAP_EVENT_CONNECT:
        ESP_LOGI("GAP", "BLE GAP EVENT CONNECT %s", event->connect.status == 0 ? "OK!" : "FAILED!");
        if (event->connect.status != 0)
        {
            ble_app_advertise();
        }
        break;
    // Advertise again after completion of the event
    case BLE_GAP_EVENT_DISCONNECT:
        ESP_LOGI("GAP", "BLE GAP EVENT DISCONNECTED");
        ble_app_advertise();
        break;
    case BLE_GAP_EVENT_ADV_COMPLETE:
        ESP_LOGI("GAP", "BLE GAP EVENT");
        ble_app_advertise();
        break;
    default:
        break;
    }
    return 0;
}

void ble_app_advertise(void){
    struct ble_hs_adv_fields fields;
    const char *device_name;
    memset(&fields, 0, sizeof(fields));
    device_name = ble_svc_gap_device_name();
    fields.name = (uint8_t *)device_name;
    fields.name_len = strlen(device_name);
    fields.name_is_complete = 1;
    ble_gap_adv_set_fields(&fields);

    // struct ble_gap_adv_params adv_params;
    memset(&adv_params, 0, sizeof(adv_params));
    // adv_params.conn_mode = BLE_GAP_CONN_MODE_UND; // connectable or non-connectable
    // adv_params.disc_mode = BLE_GAP_DISC_MODE_GEN; // discoverable or non-discoverable
    // ble_gap_adv_start(ble_addr_type, NULL, BLE_HS_FOREVER, &adv_params, ble_gap_event, NULL);
    
}

// The application
void ble_app_on_sync(void)
{
    ble_hs_id_infer_auto(0, &ble_addr_type); // Determines the best address type automatically
    ble_app_advertise();                     // Define the BLE connection
}

// The infinite task
void host_task(void *param)
{
    nimble_port_run(); // This function will return only when nimble_port_stop() is executed
}

void init_ble_with_boot(void *param)
{

    // printf("%lld\n", n - m);
    int64_t m = esp_timer_get_time();
    while (1)
    {

        if (!gpio_get_level(GPIO_NUM_0))
        {
            int64_t n = esp_timer_get_time();

            if ((n - m) / 1000 >= 2000)
            {
                ESP_LOGI("BOOT BUTTON:", "Button Pressed FOR 3 SECOND\n");

                adv_params.conn_mode = BLE_GAP_CONN_MODE_UND; // connectable or non-connectable
                adv_params.disc_mode = BLE_GAP_DISC_MODE_GEN; // discoverable or non-discoverable
                ble_gap_adv_start(ble_addr_type, NULL, BLE_HS_FOREVER, &adv_params, ble_gap_event, NULL);
                status = true;
                vTaskDelay(100);
                m = esp_timer_get_time();
            }
        }
        else
        {
            m = esp_timer_get_time();
        }
        vTaskDelay(10);

        if (status)
        {
            // ESP_LOGI("GAP", "BLE GAP status");
            adv_params.conn_mode = BLE_GAP_CONN_MODE_UND; // connectable or non-connectable
            adv_params.disc_mode = BLE_GAP_DISC_MODE_GEN; // discoverable or non-discoverable
            ble_gap_adv_start(ble_addr_type, NULL, BLE_HS_FOREVER, &adv_params, ble_gap_event, NULL);
        }
    }
}

void Init_nimBLE_Sequence(void)
{
    /*
    *  nimBLE init
    */
    //
    nvs_flash_init();     //Init NVS flash because ESP usues it during initialization (ESP-documentation)
    nimble_port_init();   //Initialize host stack (ESP-documentation)
    /*
    *  Required nimBLE host configuration and app specific initialization
    */
    ble_svc_gap_device_name_set(TAG);
    ble_svc_gap_init();
    ble_svc_gatt_init();
    ble_gatts_count_cfg(gatt_cfg);
    ble_gatts_add_svcs(gatt_cfg);
    ble_hs_cfg.sync_cb = ble_app_on_sync;
    /*
    *  Run the thread for host stack
    */
    nimble_port_freertos_init(host_task);   //Run the thread for host stack (ESP-documentation)
    //GPIO config
    gpio_set_direction(GPIO_NUM_0, GPIO_MODE_INPUT);
    xTaskCreate(init_ble_with_boot,"init_ble_with_boot", 2048, NULL, 5, NULL);
}