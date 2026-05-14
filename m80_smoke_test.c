#ifdef _WIN32
#include <WinSock2.h>
#include <Windows.h>
#endif

#ifndef _WIN32
#include <unistd.h>
#endif

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>

#include "m70_error.h"
#include "m70_ezsocket.h"

#define MAX_AXES 16
#define MAX_AXIS_NAME_LEN 32
#define AXIS_NAMES_BUFFER_SIZE 256

static const char *status_to_str(m70_device_status_e v)
{
    switch (v)
    {
    case STOP:
        return "STOP";
    case RUN:
        return "RUN";
    case IDLE:
        return "IDLE";
    case OFFLINE:
        return "OFFLINE";
    case DEBUG:
        return "DEBUG";
    default:
        return "UNKNOWN";
    }
}

static void sleep_ms(int ms)
{
#ifdef _WIN32
    Sleep(ms);
#else
    usleep(ms * 1000);
#endif
}

static void json_escape(const char *in, char *out, size_t out_size)
{
    size_t i = 0;
    size_t j = 0;
    if (out_size == 0)
    {
        return;
    }

    while (in[i] != '\0' && j + 1 < out_size)
    {
        char c = in[i++];
        if (c == '"' || c == '\\')
        {
            if (j + 2 >= out_size)
            {
                break;
            }
            out[j++] = '\\';
            out[j++] = c;
            continue;
        }
        if ((unsigned char)c < 0x20)
        {
            if (j + 2 >= out_size)
            {
                break;
            }
            out[j++] = ' ';
            continue;
        }
        out[j++] = c;
    }
    out[j] = '\0';
}

static uint64_t current_epoch_ms(void)
{
#ifdef _WIN32
    FILETIME file_time;
    ULARGE_INTEGER ticks;
    GetSystemTimeAsFileTime(&file_time);
    ticks.LowPart = file_time.dwLowDateTime;
    ticks.HighPart = file_time.dwHighDateTime;
    return (ticks.QuadPart - 116444736000000000ULL) / 10000ULL;
#else
    struct timespec ts;
    clock_gettime(CLOCK_REALTIME, &ts);
    return (uint64_t)ts.tv_sec * 1000ULL + (uint64_t)(ts.tv_nsec / 1000000ULL);
#endif
}

static uint64_t current_tick_ms(void)
{
#ifdef _WIN32
    return (uint64_t)GetTickCount64();
#else
    struct timespec ts;
    clock_gettime(CLOCK_MONOTONIC, &ts);
    return (uint64_t)ts.tv_sec * 1000ULL + (uint64_t)(ts.tv_nsec / 1000000ULL);
#endif
}

static void trim_ascii_whitespace(char *text)
{
    char *start = text;
    char *end = NULL;

    while (*start == ' ' || *start == '\t' || *start == '\r' || *start == '\n')
    {
        start++;
    }

    if (start != text)
    {
        memmove(text, start, strlen(start) + 1);
    }

    end = text + strlen(text);
    while (end > text)
    {
        char c = *(end - 1);
        if (c != ' ' && c != '\t' && c != '\r' && c != '\n')
        {
            break;
        }
        end--;
    }

    *end = '\0';
}

static void assign_default_axis_names(char axis_names[MAX_AXES][MAX_AXIS_NAME_LEN], int axis_count)
{
    int i = 0;
    for (i = 0; i < axis_count && i < MAX_AXES; i++)
    {
        snprintf(axis_names[i], MAX_AXIS_NAME_LEN, "Axis%d", i + 1);
    }
}

static void parse_axis_names(const char *raw_names, char axis_names[MAX_AXES][MAX_AXIS_NAME_LEN], int axis_count)
{
    char buffer[AXIS_NAMES_BUFFER_SIZE];
    char *token = NULL;
    int index = 0;

    assign_default_axis_names(axis_names, axis_count);

    if (raw_names == NULL || raw_names[0] == '\0' || axis_count <= 0)
    {
        return;
    }

    strncpy(buffer, raw_names, sizeof(buffer) - 1);
    buffer[sizeof(buffer) - 1] = '\0';

#ifdef _WIN32
    char *context = NULL;
    token = strtok_s(buffer, ",", &context);
#else
    char *context = NULL;
    token = strtok_r(buffer, ",", &context);
#endif
    while (token != NULL && index < axis_count && index < MAX_AXES)
    {
        char cleaned[MAX_AXIS_NAME_LEN];
        strncpy(cleaned, token, sizeof(cleaned) - 1);
        cleaned[sizeof(cleaned) - 1] = '\0';
        trim_ascii_whitespace(cleaned);
        if (cleaned[0] != '\0')
        {
            strncpy(axis_names[index], cleaned, MAX_AXIS_NAME_LEN - 1);
            axis_names[index][MAX_AXIS_NAME_LEN - 1] = '\0';
        }
        index++;
#ifdef _WIN32
        token = strtok_s(NULL, ",", &context);
#else
        token = strtok_r(NULL, ",", &context);
#endif
    }
}

static void print_axis_names_json(char axis_names[MAX_AXES][MAX_AXIS_NAME_LEN], int axis_count)
{
    int i = 0;
    printf("\"axis_names\":[");
    for (i = 0; i < axis_count && i < MAX_AXES; i++)
    {
        char escaped[MAX_AXIS_NAME_LEN * 2];
        if (i > 0)
        {
            printf(",");
        }
        json_escape(axis_names[i], escaped, sizeof(escaped));
        printf("\"%s\"", escaped);
    }
    printf("],");
}

int main(int argc, char **argv)
{
#ifdef _WIN32
    WSADATA wsa;
    if (WSAStartup(MAKEWORD(2, 2), &wsa) != 0)
    {
        fprintf(stderr, "WSAStartup failed\n");
        return 1;
    }
#endif

    if (argc < 2)
    {
        fprintf(stderr, "Usage: %s <ip> [port] [nc_type] [interval_ms] [samples]\n", argv[0]);
        fprintf(stderr, "  default port: 683\n");
        fprintf(stderr, "  default nc_type: 8 (EZNC_SYS_MELDAS800M, for M80/M800)\n");
        fprintf(stderr, "  default interval_ms: 1000\n");
        fprintf(stderr, "  default samples: 0 (infinite)\n");
        return 1;
    }

    const char *ip = argv[1];
    int port = (argc >= 3) ? atoi(argv[2]) : 683;
    m70_nc_type_e nc_type = (argc >= 4) ? (m70_nc_type_e)atoi(argv[3]) : EZNC_SYS_MELDAS800M;
    int interval_ms = (argc >= 5) ? atoi(argv[4]) : 1000;
    int samples = (argc >= 6) ? atoi(argv[5]) : 0;
    if (interval_ms <= 0)
    {
        interval_ms = 1000;
    }

    m70_conn_t conn = {0};
    if (!m70_cnc_connect(ip, port, nc_type, &conn) || conn.socket <= 0)
    {
        fprintf(stderr, "Connect failed: ip=%s port=%d nc_type=%d\n", ip, port, (int)nc_type);
        return 2;
    }

    printf("Connected: ip=%s port=%d nc_type=%d\n", ip, port, (int)nc_type);
    printf("Polling started: interval_ms=%d samples=%d\n", interval_ms, samples);

    uint32 nc_axis_count_u32 = 0;
    m70_error_code_e ret_axis_count = m70_cnc_read_nc_axis_count(&conn, &nc_axis_count_u32);
    int axis_count = 0;
    char axis_names_raw[AXIS_NAMES_BUFFER_SIZE] = {0};
    char axis_names[MAX_AXES][MAX_AXIS_NAME_LEN] = {{0}};
    if (ret_axis_count == M70_ERROR_CODE_OK)
    {
        axis_count = (int)nc_axis_count_u32;
        if (axis_count < 0)
        {
            axis_count = 0;
        }
        if (axis_count > MAX_AXES)
        {
            axis_count = MAX_AXES;
        }
    }
    assign_default_axis_names(axis_names, axis_count);
    if (axis_count > 0)
    {
        int axis_name_count = axis_count;
        if (m70_cnc_read_axis_name(&conn, 1, axis_names_raw, &axis_name_count) == M70_ERROR_CODE_OK)
        {
            if (axis_name_count > 0 && axis_name_count < axis_count)
            {
                axis_count = axis_name_count;
            }
            parse_axis_names(axis_names_raw, axis_names, axis_count);
        }
    }

    double prev_axis_pos[MAX_AXES] = {0.0};
    int have_prev_pos = 0;
    uint64_t previous_sample_tick_ms = 0;
    uint64_t next_sample_tick_ms = current_tick_ms();

    int i = 0;
    while (samples == 0 || i < samples)
    {
        uint64_t sample_tick_ms = current_tick_ms();
        uint64_t sample_epoch_ms = 0;
        double axis_elapsed_ms = 0.0;
        m70_error_code_e ret_status;
        m70_error_code_e ret_spindle;
        m70_error_code_e ret_feed;
        m70_error_code_e ret_counter;
        m70_error_code_e ret_tool;
        m70_error_code_e ret_alarm;
        m70_error_code_e ret_spindle_torque;
        m70_error_code_e ret_axis_pos = M70_ERROR_CODE_FAILED;

        m70_device_status_e status = Unkown;
        m70_run_mode_e mode = MEM;
        m70_run_status_e run_status = RST;
        uint32 spindle_speed = 0;
        int32 spindle_torque_load = 0;
        double feed_speed = 0.0;
        uint32 counter = 0;
        uint32 tool_no = 0;
        alarm_string alarm = {0};
        double axis_pos[MAX_AXES] = {0.0};
        double axis_feed_rate[MAX_AXES] = {0.0};
        short axis_torque[MAX_AXES] = {0};
        int axis_torque_ok[MAX_AXES] = {0};
        int axis_pos_count = axis_count;
        int axis_torque_fail_count = 0;

        int alarm_active = 0;
        int alarm_no = 0;
        char alarm_text[256] = {0};
        char alarm_text_json[512] = {0};

        if (previous_sample_tick_ms > 0 && sample_tick_ms > previous_sample_tick_ms)
        {
            axis_elapsed_ms = (double)(sample_tick_ms - previous_sample_tick_ms);
        }

        ret_status = m70_cnc_read_status(&conn, 1, &status, &mode, &run_status);
        ret_spindle = m70_cnc_read_spindle_speed(&conn, 1, &spindle_speed, 1);
        ret_spindle_torque = m70_cnc_read_spindle_load(&conn, 1, &spindle_torque_load, 1, false);
        ret_feed = m70_cnc_read_feed_speed(&conn, 1, &feed_speed, FC);
        ret_counter = m70_cnc_read_counter(&conn, 1, &counter);
        ret_tool = m70_cnc_read_current_tool_no(&conn, 1, &tool_no);
        ret_alarm = m70_cnc_read_alarm(&conn, 1, 1, M_ALM_ALL_ALARM, &alarm);
        if (axis_count > 0)
        {
            ret_axis_pos = m70_cnc_read_all_axis_position(&conn, 1, axis_pos, &axis_pos_count, POS_MCH);
            if (ret_axis_pos == M70_ERROR_CODE_OK && axis_pos_count > 0 && axis_pos_count <= axis_count)
            {
                int k = 0;
                for (k = 0; k < axis_pos_count; k++)
                {
                    axis_feed_rate[k] = (have_prev_pos && axis_elapsed_ms > 0.0)
                        ? (axis_pos[k] - prev_axis_pos[k]) * 1000.0 / axis_elapsed_ms
                        : 0.0;
                }
                for (k = axis_pos_count; k < axis_count; k++)
                {
                    axis_feed_rate[k] = 0.0;
                }
                for (k = 0; k < axis_pos_count; k++)
                {
                    prev_axis_pos[k] = axis_pos[k];
                }
                have_prev_pos = 1;
            }
        }

        if (axis_count > 0)
        {
            int a = 0;
            for (a = 0; a < axis_count; a++)
            {
                m70_error_code_e ret_axis_torque = m70_cnc_read_svo_load(&conn, 1, &axis_torque[a], (uint32)(a + 1), false);
                if (ret_axis_torque == M70_ERROR_CODE_OK)
                {
                    axis_torque_ok[a] = 1;
                }
                else
                {
                    axis_torque_ok[a] = 0;
                    axis_torque_fail_count++;
                }
            }
        }

        if (ret_alarm == M70_ERROR_CODE_OK && alarm.alarm_length > 0)
        {
            int n = alarm.alarm_length;
            if (n > 255)
            {
                n = 255;
            }
            memcpy(alarm_text, alarm.text, (size_t)n);
            alarm_text[n] = '\0';
            alarm_active = 1;
            alarm_no = alarm.alarm_no;
        }

        json_escape(alarm_text, alarm_text_json, sizeof(alarm_text_json));
        sample_epoch_ms = current_epoch_ms();

        printf("{\"ts\":%llu,\"ts_ms\":%llu,\"sample\":%d,", (unsigned long long)(sample_epoch_ms / 1000ULL), (unsigned long long)sample_epoch_ms, i + 1);
        printf("\"status\":%d,\"status_text\":\"%s\",\"mode\":%d,\"run_status\":%d,", status, status_to_str(status), mode, run_status);
        printf("\"spindle_speed\":%u,\"spindle_torque_load\":%d,", spindle_speed, spindle_torque_load);
        printf("\"feed_speed\":%.3f,\"counter\":%u,\"part_count\":%u,", feed_speed, counter, counter);
        if (ret_tool == M70_ERROR_CODE_OK)
        {
            printf("\"tool_number\":%u,", tool_no);
        }
        else
        {
            printf("\"tool_number\":null,");
        }
        printf("\"axis_count\":%d,", axis_count);
        print_axis_names_json(axis_names, axis_count);
        printf("\"axis_torque\":[");
        if (axis_count > 0)
        {
            int a = 0;
            for (a = 0; a < axis_count; a++)
            {
                if (a > 0)
                {
                    printf(",");
                }
                if (axis_torque_ok[a])
                {
                    printf("%d", axis_torque[a]);
                }
                else
                {
                    printf("null");
                }
            }
        }
        printf("],");
        printf("\"axis_feed_rate\":[");
        if (axis_count > 0)
        {
            int a = 0;
            for (a = 0; a < axis_count; a++)
            {
                if (a > 0)
                {
                    printf(",");
                }
                if (ret_axis_pos == M70_ERROR_CODE_OK && a < axis_pos_count)
                {
                    printf("%.3f", axis_feed_rate[a]);
                }
                else
                {
                    printf("null");
                }
            }
        }
        printf("],");
        printf("\"alarm_active\":%s,\"alarm_no\":%d,\"alarm_text\":\"%s\",", alarm_active ? "true" : "false", alarm_no, alarm_text_json);
        printf("\"ret\":{\"status\":%d,\"spindle\":%d,\"spindle_torque\":%d,\"feed\":%d,\"counter\":%d,\"tool\":%d,\"alarm\":%d,\"axis_count\":%d,\"axis_pos\":%d,\"axis_torque_fail_count\":%d}}\n",
               ret_status, ret_spindle, ret_spindle_torque, ret_feed, ret_counter, ret_tool, ret_alarm, ret_axis_count, ret_axis_pos, axis_torque_fail_count);
        fflush(stdout);

        previous_sample_tick_ms = sample_tick_ms;
        i++;
        if (samples == 0 || i < samples)
        {
            uint64_t now_tick_ms = current_tick_ms();
            next_sample_tick_ms += (uint64_t)interval_ms;
            if (now_tick_ms < next_sample_tick_ms)
            {
                sleep_ms((int)(next_sample_tick_ms - now_tick_ms));
            }
            else
            {
                next_sample_tick_ms = now_tick_ms;
            }
        }
    }

    m70_cnc_disconnect(&conn);

#ifdef _WIN32
    WSACleanup();
#endif
    return 0;
}
