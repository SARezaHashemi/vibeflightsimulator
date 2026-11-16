/**
 * Keyboard input, reduced to four normalised axes.
 *
 * Pitch follows stick convention: pull back (S / ↓) raises the nose.
 */

const CAPTURED = new Set([
  'ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'Space',
  'KeyW', 'KeyA', 'KeyS', 'KeyD', 'KeyQ', 'KeyE', 'KeyX', 'KeyR', 'KeyG', 'KeyC',
  'ShiftLeft', 'ShiftRight',
]);

const AXES = {
  pitch:    { negative: ['KeyW', 'ArrowUp'],   positive: ['KeyS', 'ArrowDown'] },
  roll:     { negative: ['KeyA', 'ArrowLeft'], positive: ['KeyD', 'ArrowRight'] },
  yaw:      { negative: ['KeyQ'],              positive: ['KeyE'] },
  throttle: { negative: ['KeyX'],              positive: ['ShiftLeft', 'ShiftRight'] },
};

const ZERO = { pitch: 0, roll: 0, yaw: 0, throttle: 0 };

export class Input {
  #down = new Set();

  /**
   * @param {Window|HTMLElement} target
   * @param {{ onReset?: () => void, onGear?: () => void, onView?: () => void }} [handlers]
   *   Commands (as opposed to held controls) are reported as key events.
   */
  constructor(target = window, handlers = {}) {
    target.addEventListener('keydown', (event) => {
      if (CAPTURED.has(event.code)) event.preventDefault();
      if (event.repeat) return;

      this.#down.add(event.code);
      if (event.code === 'KeyR' && handlers.onReset) handlers.onReset();
      if (event.code === 'KeyG' && handlers.onGear) handlers.onGear();
      if (event.code === 'KeyC' && handlers.onView) handlers.onView();
    });

    target.addEventListener('keyup', (event) => {
      this.#down.delete(event.code);
    });

    // Alt-tabbing away must not leave a key stuck down.
    target.addEventListener('blur', () => this.#down.clear());
  }

  isDown(code) {
    return this.#down.has(code);
  }

  /** Current control positions, each in [-1, 1]. */
  get axes() {
    const state = this.#down;
    if (state.size === 0) return ZERO;

    const read = (bind) => {
      let value = 0;
      for (const code of bind.negative) if (state.has(code)) value -= 1;
      for (const code of bind.positive) if (state.has(code)) value += 1;
      return value;
    };

    return {
      pitch: read(AXES.pitch),
      roll: read(AXES.roll),
      yaw: read(AXES.yaw),
      throttle: read(AXES.throttle),
    };
  }
}
