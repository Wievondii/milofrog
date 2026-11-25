from pathlib import Path
import math
import sys

import cv2
import numpy as np
from PIL import Image


WIDTH, HEIGHT, COLUMNS = 192, 208, 16
COUNTS = (6, 8, 8, 4, 5, 8, 6, 6, 6, 8, 8)
TIMING_MS = (175, 95, 95, 160, 145, 145, 160, 155, 160)
FPS = 30
Y, X = np.mgrid[:HEIGHT, :WIDTH].astype(np.float32)


def premultiply(rgba):
    result = rgba.astype(np.float32) / 255
    result[:, :, :3] *= result[:, :, 3:]
    return result


def flow_image(rgba):
    rgb = rgba[:, :, :3] + (1 - rgba[:, :, 3:]) * 0.18
    return cv2.cvtColor(np.round(rgb * 255).astype(np.uint8), cv2.COLOR_RGB2GRAY)


def warp(image, flow, amount):
    return cv2.remap(image, X - amount * flow[:, :, 0], Y - amount * flow[:, :, 1],
                     cv2.INTER_LINEAR, borderMode=cv2.BORDER_CONSTANT)


def interpolate(a, b, forward, reverse, t):
    first = warp(a, forward, t)
    second = warp(b, reverse, 1 - t)
    # 大幅遮挡和表情变化由较近的原画提供，避免两条腿或两张嘴叠在一起。
    disagreement = np.max(np.abs(first - second), axis=2)
    disagreement = cv2.dilate(disagreement, np.ones((3, 3), dtype=np.uint8))
    selection = np.clip((disagreement - 0.09) / 0.20, 0, 1)[:, :, None]
    weight = t + selection * ((0 if t < 0.5 else 1) - t)
    mixed = first * (1 - weight) + second * weight
    body = (mixed[:, :, 3] > 0.23).astype(np.uint8)
    labels, regions, stats, _ = cv2.connectedComponentsWithStats(body, connectivity=8)
    if labels > 2:
        main = 1 + np.argmax(stats[1:, cv2.CC_STAT_AREA])
        nearby = cv2.dilate((regions == main).astype(np.uint8), np.ones((5, 5), dtype=np.uint8))
        mixed *= nearby[:, :, None]
    alpha = mixed[:, :, 3:]
    mixed[:, :, :3] = np.divide(mixed[:, :, :3], alpha, out=np.zeros_like(mixed[:, :, :3]), where=alpha > 0)
    return Image.fromarray(np.round(np.clip(mixed, 0, 1) * 255).astype(np.uint8))


def expand(images, count, loop):
    frames = [premultiply(np.asarray(image)) for image in images]
    grayscale = [flow_image(frame) for frame in frames]
    dis = cv2.DISOpticalFlow_create(cv2.DISOPTICAL_FLOW_PRESET_MEDIUM)
    pairs = {}
    for tick in range(count):
        phase = tick * len(images) / count
        left = int(phase)
        right = (left + 1) % len(images) if loop else min(left + 1, len(images) - 1)
        fraction = phase - left
        if fraction == 0 or left == right:
            yield images[left]
            continue
        if left not in pairs:
            first, second = grayscale[left], grayscale[right]
            pairs[left] = dis.calc(first, second, None), dis.calc(second, first, None)
        yield interpolate(frames[left], frames[right], *pairs[left], fraction)


def main():
    source = Image.open(sys.argv[1]).convert('RGBA')
    output = Path(sys.argv[2])
    result = []
    lengths = []
    for row, keys in enumerate(COUNTS):
        images = [source.crop((x * WIDTH, row * HEIGHT, (x + 1) * WIDTH, (row + 1) * HEIGHT)) for x in range(keys)]
        frames = list(expand(images, round(keys * TIMING_MS[row] * FPS / 1000), row <= 2)) if row < 9 else images
        lengths.append(len(frames))
        result.extend(frames)
    atlas = Image.new('RGBA', (COLUMNS * WIDTH, math.ceil(len(result) / COLUMNS) * HEIGHT))
    for index, frame in enumerate(result):
        atlas.paste(frame, (index % COLUMNS * WIDTH, index // COLUMNS * HEIGHT))
    atlas.save(output, optimize=True)
    print(lengths, len(result))


if __name__ == '__main__':
    main()
