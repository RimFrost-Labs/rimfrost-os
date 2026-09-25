/*
 * RimFrost frame meter: a tiny Vulkan layer that measures frame pacing.
 *
 * It only watches vkQueuePresentKHR. Once a second it appends one line to
 * $RIMFROST_FRAMEMETER_DIR/<pid>.log (default /var/tmp/rimfrost-frames):
 *
 *     <unix_ms> <fps> <avg_frametime_ms> <1%_low_fps> <max_frametime_ms> <exe>
 *
 * The layer is implicit but only active when RIMFROST_FRAMEMETER=1, which
 * rimfrost-telemetry sets on enrolled test machines. It draws nothing and
 * changes nothing the game sends to the driver.
 *
 * SPDX-License-Identifier: Apache-2.0
 */
#include <vulkan/vulkan.h>
#include <vulkan/vk_layer.h>

#include <errno.h>
#include <pthread.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/stat.h>
#include <time.h>
#include <unistd.h>

#define EXPORT __attribute__((visibility("default")))
#define MAX_OBJS 64
#define MAX_FRAMES 4096

/* dispatch key = first pointer of any dispatchable handle */
static inline void *key(const void *h) { return *(void **)h; }

struct inst { void *key; PFN_vkGetInstanceProcAddr gipa; PFN_vkDestroyInstance destroy; };
struct dev { void *key; PFN_vkGetDeviceProcAddr gdpa; PFN_vkQueuePresentKHR present; PFN_vkDestroyDevice destroy; };

static struct inst insts[MAX_OBJS];
static struct dev devs[MAX_OBJS];
static pthread_mutex_t lock = PTHREAD_MUTEX_INITIALIZER;

/* frame statistics for the current second */
static double frame_ms[MAX_FRAMES];
static int nframes;
static double last_present, window_start;
static FILE *out;
static char exe[64];

static double now_ms(void)
{
    struct timespec ts;
    clock_gettime(CLOCK_MONOTONIC, &ts);
    return ts.tv_sec * 1e3 + ts.tv_nsec / 1e6;
}

static int cmp_desc(const void *a, const void *b)
{
    double x = *(const double *)a, y = *(const double *)b;
    return (x < y) - (x > y);
}

static void open_log(void)
{
    const char *dir = getenv("RIMFROST_FRAMEMETER_DIR");
    char path[512];
    if (!dir || !*dir)
        dir = "/var/tmp/rimfrost-frames";
    mkdir(dir, 01777);
    snprintf(path, sizeof path, "%s/%d.log", dir, (int)getpid());
    out = fopen(path, "a");
    if (out)
        setvbuf(out, NULL, _IOLBF, 0);
    FILE *f = fopen("/proc/self/comm", "r");
    if (f) {
        if (fgets(exe, sizeof exe, f))
            exe[strcspn(exe, "\n ")] = 0;
        fclose(f);
    }
}

static void flush_second(double t)
{
    if (!out || nframes == 0)
        return;
    double sum = 0;
    for (int i = 0; i < nframes; i++)
        sum += frame_ms[i];
    qsort(frame_ms, nframes, sizeof(double), cmp_desc);
    int worst_n = nframes / 100 > 0 ? nframes / 100 : 1;
    double worst = 0;
    for (int i = 0; i < worst_n; i++)
        worst += frame_ms[i];
    worst /= worst_n;
    struct timespec real;
    clock_gettime(CLOCK_REALTIME, &real);
    double secs = (t - window_start) / 1e3;
    fprintf(out, "%lld %.1f %.2f %.1f %.2f %s\n",
            (long long)real.tv_sec * 1000 + real.tv_nsec / 1000000,
            nframes / (secs > 0 ? secs : 1), sum / nframes, 1000.0 / worst, frame_ms[0], exe);
    nframes = 0;
    window_start = t;
}

static void count_frame(void)
{
    double t = now_ms();
    pthread_mutex_lock(&lock);
    if (!out && !window_start)
        open_log();
    if (last_present > 0 && nframes < MAX_FRAMES)
        frame_ms[nframes++] = t - last_present;
    if (window_start == 0)
        window_start = t;
    last_present = t;
    if (t - window_start >= 1000)
        flush_second(t);
    pthread_mutex_unlock(&lock);
}

static struct inst *find_inst(void *k)
{
    for (int i = 0; i < MAX_OBJS; i++)
        if (insts[i].key == k)
            return &insts[i];
    return NULL;
}

static struct dev *find_dev(void *k)
{
    for (int i = 0; i < MAX_OBJS; i++)
        if (devs[i].key == k)
            return &devs[i];
    return NULL;
}

/* --- hooked functions --------------------------------------------------------- */

static VKAPI_ATTR VkResult VKAPI_CALL fm_QueuePresentKHR(VkQueue queue, const VkPresentInfoKHR *info)
{
    pthread_mutex_lock(&lock);
    struct dev *d = find_dev(key(queue));
    PFN_vkQueuePresentKHR next = d ? d->present : NULL;
    pthread_mutex_unlock(&lock);
    count_frame();
    return next ? next(queue, info) : VK_ERROR_DEVICE_LOST;
}

static VKAPI_ATTR void VKAPI_CALL fm_DestroyDevice(VkDevice device, const VkAllocationCallbacks *alloc)
{
    pthread_mutex_lock(&lock);
    struct dev *d = find_dev(key(device));
    PFN_vkDestroyDevice next = d ? d->destroy : NULL;
    if (d)
        memset(d, 0, sizeof *d);
    pthread_mutex_unlock(&lock);
    if (next)
        next(device, alloc);
}

static VKAPI_ATTR VkResult VKAPI_CALL fm_CreateDevice(VkPhysicalDevice phys, const VkDeviceCreateInfo *ci,
                                                      const VkAllocationCallbacks *alloc, VkDevice *out_dev)
{
    VkLayerDeviceCreateInfo *link = (VkLayerDeviceCreateInfo *)ci->pNext;
    while (link && !(link->sType == VK_STRUCTURE_TYPE_LOADER_DEVICE_CREATE_INFO && link->function == VK_LAYER_LINK_INFO))
        link = (VkLayerDeviceCreateInfo *)link->pNext;
    if (!link)
        return VK_ERROR_INITIALIZATION_FAILED;
    PFN_vkGetInstanceProcAddr gipa = link->u.pLayerInfo->pfnNextGetInstanceProcAddr;
    PFN_vkGetDeviceProcAddr gdpa = link->u.pLayerInfo->pfnNextGetDeviceProcAddr;
    link->u.pLayerInfo = link->u.pLayerInfo->pNext;
    PFN_vkCreateDevice create = (PFN_vkCreateDevice)gipa(VK_NULL_HANDLE, "vkCreateDevice");
    VkResult r = create(phys, ci, alloc, out_dev);
    if (r != VK_SUCCESS)
        return r;
    pthread_mutex_lock(&lock);
    for (int i = 0; i < MAX_OBJS; i++) {
        if (!devs[i].key) {
            devs[i].key = key(*out_dev);
            devs[i].gdpa = gdpa;
            devs[i].present = (PFN_vkQueuePresentKHR)gdpa(*out_dev, "vkQueuePresentKHR");
            devs[i].destroy = (PFN_vkDestroyDevice)gdpa(*out_dev, "vkDestroyDevice");
            break;
        }
    }
    pthread_mutex_unlock(&lock);
    return VK_SUCCESS;
}

static VKAPI_ATTR void VKAPI_CALL fm_DestroyInstance(VkInstance instance, const VkAllocationCallbacks *alloc)
{
    pthread_mutex_lock(&lock);
    struct inst *in = find_inst(key(instance));
    PFN_vkDestroyInstance next = in ? in->destroy : NULL;
    if (in)
        memset(in, 0, sizeof *in);
    pthread_mutex_unlock(&lock);
    if (next)
        next(instance, alloc);
}

static VKAPI_ATTR VkResult VKAPI_CALL fm_CreateInstance(const VkInstanceCreateInfo *ci,
                                                        const VkAllocationCallbacks *alloc, VkInstance *out_inst)
{
    VkLayerInstanceCreateInfo *link = (VkLayerInstanceCreateInfo *)ci->pNext;
    while (link && !(link->sType == VK_STRUCTURE_TYPE_LOADER_INSTANCE_CREATE_INFO && link->function == VK_LAYER_LINK_INFO))
        link = (VkLayerInstanceCreateInfo *)link->pNext;
    if (!link)
        return VK_ERROR_INITIALIZATION_FAILED;
    PFN_vkGetInstanceProcAddr gipa = link->u.pLayerInfo->pfnNextGetInstanceProcAddr;
    link->u.pLayerInfo = link->u.pLayerInfo->pNext;
    PFN_vkCreateInstance create = (PFN_vkCreateInstance)gipa(VK_NULL_HANDLE, "vkCreateInstance");
    VkResult r = create(ci, alloc, out_inst);
    if (r != VK_SUCCESS)
        return r;
    pthread_mutex_lock(&lock);
    for (int i = 0; i < MAX_OBJS; i++) {
        if (!insts[i].key) {
            insts[i].key = key(*out_inst);
            insts[i].gipa = gipa;
            insts[i].destroy = (PFN_vkDestroyInstance)gipa(*out_inst, "vkDestroyInstance");
            break;
        }
    }
    pthread_mutex_unlock(&lock);
    return VK_SUCCESS;
}

/* --- entry points ------------------------------------------------------------ */

EXPORT VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL fm_GetDeviceProcAddr(VkDevice device, const char *name)
{
    if (!strcmp(name, "vkGetDeviceProcAddr")) return (PFN_vkVoidFunction)fm_GetDeviceProcAddr;
    if (!strcmp(name, "vkQueuePresentKHR")) return (PFN_vkVoidFunction)fm_QueuePresentKHR;
    if (!strcmp(name, "vkDestroyDevice")) return (PFN_vkVoidFunction)fm_DestroyDevice;
    pthread_mutex_lock(&lock);
    struct dev *d = find_dev(key(device));
    PFN_vkGetDeviceProcAddr next = d ? d->gdpa : NULL;
    pthread_mutex_unlock(&lock);
    return next ? next(device, name) : NULL;
}

EXPORT VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL fm_GetInstanceProcAddr(VkInstance instance, const char *name)
{
    if (!strcmp(name, "vkGetInstanceProcAddr")) return (PFN_vkVoidFunction)fm_GetInstanceProcAddr;
    if (!strcmp(name, "vkCreateInstance")) return (PFN_vkVoidFunction)fm_CreateInstance;
    if (!strcmp(name, "vkDestroyInstance")) return (PFN_vkVoidFunction)fm_DestroyInstance;
    if (!strcmp(name, "vkCreateDevice")) return (PFN_vkVoidFunction)fm_CreateDevice;
    if (!strcmp(name, "vkGetDeviceProcAddr")) return (PFN_vkVoidFunction)fm_GetDeviceProcAddr;
    if (!strcmp(name, "vkQueuePresentKHR")) return (PFN_vkVoidFunction)fm_QueuePresentKHR;
    if (!strcmp(name, "vkDestroyDevice")) return (PFN_vkVoidFunction)fm_DestroyDevice;
    if (!instance)
        return NULL;
    pthread_mutex_lock(&lock);
    struct inst *in = find_inst(key(instance));
    PFN_vkGetInstanceProcAddr next = in ? in->gipa : NULL;
    pthread_mutex_unlock(&lock);
    return next ? next(instance, name) : NULL;
}
