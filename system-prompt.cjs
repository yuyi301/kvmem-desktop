const fs = require('node:fs');
const path = require('node:path');

function readSystemPrompt(root = __dirname) {
  const file = path.join(root, 'system-prompt.json');
  try {
    const value = JSON.parse(fs.readFileSync(file, 'utf8').replace(/^\uFEFF/, ''));
    if (typeof value.Text !== 'string') throw new Error('Text must be a string');
    return value.Text;
  } catch (error) {
    if (error.code === 'ENOENT') return '';
    throw new Error('Cannot read system-prompt.json: ' + error.message);
  }
}

function withSystemPrompt(messages, prompt) {
  if (!Array.isArray(messages)) throw new Error('messages must be an array');
  if (!prompt.trim() || messages.some(m => m && ['system', 'developer'].includes(m.role))) return messages;
  return [{role: 'system', content: prompt}, ...messages];
}

module.exports = {readSystemPrompt, withSystemPrompt};
