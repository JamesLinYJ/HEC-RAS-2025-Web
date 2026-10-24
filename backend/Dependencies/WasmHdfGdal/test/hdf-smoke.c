#include <hdf5.h>
#include <pthread.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>

enum { THREAD_COUNT = 4, DATASET_LENGTH = 64 };

static int write_and_read_file(unsigned int index) {
    char path[48];
    (void)snprintf(path, sizeof(path), "/hdf-smoke-%u.h5", index);

    hsize_t dimensions[1] = {DATASET_LENGTH};
    hsize_t chunks[1] = {16};
    int values[DATASET_LENGTH];
    int received[DATASET_LENGTH];
    for (int i = 0; i < DATASET_LENGTH; ++i) {
        values[i] = (int)(index * 1000U + (unsigned int)i);
        received[i] = 0;
    }

    hid_t file = H5Fcreate(path, H5F_ACC_TRUNC, H5P_DEFAULT, H5P_DEFAULT);
    if (file < 0) return 1;
    hid_t space = H5Screate_simple(1, dimensions, NULL);
    hid_t properties = H5Pcreate(H5P_DATASET_CREATE);
    hid_t dataset = -1;
    int failed = space < 0 || properties < 0;
    if (!failed && H5Pset_chunk(properties, 1, chunks) < 0) failed = 1;
    if (!failed && H5Pset_deflate(properties, 6) < 0) failed = 1;
    if (!failed) {
        dataset = H5Dcreate2(file, "/values", H5T_NATIVE_INT, space,
                             H5P_DEFAULT, properties, H5P_DEFAULT);
        if (dataset < 0) failed = 1;
    }
    if (!failed && H5Dwrite(dataset, H5T_NATIVE_INT, H5S_ALL, H5S_ALL,
                            H5P_DEFAULT, values) < 0) failed = 1;
    if (dataset >= 0 && H5Dclose(dataset) < 0) failed = 1;
    if (properties >= 0 && H5Pclose(properties) < 0) failed = 1;
    if (space >= 0 && H5Sclose(space) < 0) failed = 1;
    if (H5Fclose(file) < 0) failed = 1;

    if (!failed) {
        file = H5Fopen(path, H5F_ACC_RDONLY, H5P_DEFAULT);
        if (file < 0) failed = 1;
    }
    if (!failed) {
        dataset = H5Dopen2(file, "/values", H5P_DEFAULT);
        if (dataset < 0) failed = 1;
    }
    if (!failed && H5Dread(dataset, H5T_NATIVE_INT, H5S_ALL, H5S_ALL,
                           H5P_DEFAULT, received) < 0) failed = 1;
    if (!failed) {
        for (int i = 0; i < DATASET_LENGTH; ++i) {
            if (received[i] != values[i]) {
                failed = 1;
                break;
            }
        }
    }
    if (dataset >= 0 && H5Dclose(dataset) < 0) failed = 1;
    if (file >= 0 && H5Fclose(file) < 0) failed = 1;
    remove(path);
    return failed;
}

static void *worker(void *data) {
    const unsigned int index = (unsigned int)(uintptr_t)data;
    return (void *)(uintptr_t)write_and_read_file(index);
}

int main(void) {
    hbool_t thread_safe = 0;
    if (H5is_library_threadsafe(&thread_safe) < 0 || !thread_safe) {
        fprintf(stderr, "HDF5 reports thread-safe=%d\n", (int)thread_safe);
        return 2;
    }
    if (H5Zfilter_avail(H5Z_FILTER_DEFLATE) <= 0 ||
        H5Zfilter_avail(H5Z_FILTER_SZIP) <= 0) {
        fprintf(stderr, "required compression filter unavailable\n");
        return 3;
    }

    pthread_t threads[THREAD_COUNT];
    for (unsigned int i = 0; i < THREAD_COUNT; ++i) {
        if (pthread_create(&threads[i], NULL, worker,
                           (void *)(uintptr_t)(i + 1U)) != 0) {
            fprintf(stderr, "pthread_create failed at %u\n", i);
            return 4;
        }
    }
    for (unsigned int i = 0; i < THREAD_COUNT; ++i) {
        void *result = NULL;
        if (pthread_join(threads[i], &result) != 0 ||
            (uintptr_t)result != 0U) {
            fprintf(stderr, "concurrent HDF round-trip failed at %u\n", i);
            return 5;
        }
    }

    printf("PASS: HDF5 thread-safe=%d, DEFLATE and SZIP filters available, "
           "%u concurrent create/write/reopen/read round-trips matched.\n",
           (int)thread_safe, (unsigned int)THREAD_COUNT);
    return 0;
}
