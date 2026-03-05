class AudioProcessor extends AudioWorkletProcessor {
    constructor() {
        super();
        this.port.onmessage = (event) => {
        };
    }

    process(inputs, outputs, parameters) {
        const input = inputs[0];
        if (input.length > 0) {
            const inputData = input[0];

            const fromRate = sampleRate;
            const toRate = 16000;

            let dataToPost = inputData;
            if (fromRate !== toRate) {
                dataToPost = this.downsample(inputData, fromRate, toRate);
            }

            this.port.postMessage(dataToPost);
        }
        return true;

    }

    downsample(buffer, fromRate, toRate) {
        if (fromRate === toRate) return buffer;

        const ratio = fromRate / toRate;
        const newLength = Math.round(buffer.length / ratio);
        const result = new Float32Array(newLength);
        let offsetResult = 0;
        let offsetBuffer = 0;

        while (offsetResult < result.length) {
            const nextOffsetBuffer = Math.round((offsetResult + 1) * ratio);
            let accum = 0;
            let count = 0;
            for (let i = offsetBuffer; i < nextOffsetBuffer && i < buffer.length; i++) {
                accum += buffer[i];
                count++;
            }
            result[offsetResult] = count > 0 ? accum / count : 0;
            offsetResult++;
            offsetBuffer = nextOffsetBuffer;
        }
        return result;
    }
}

registerProcessor('audio-processor', AudioProcessor);