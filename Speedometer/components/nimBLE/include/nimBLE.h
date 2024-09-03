// Declared functions
void ble_app_advertise(void);
void ble_app_on_sync(void);
void host_task(void *param);
void Init_nimBLE_Sequence(void);
void init_ble_with_boot(void *param);
extern bool calibration_flag;
extern uint64_t time_diffrence;
// extern struct ble_gap_adv_params adv_params;
// extern uint8_t ble_addr_type; 
// extern int ble_gap_event(struct ble_gap_event *event, void *arg);