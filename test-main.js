const electron = require('electron')
console.log('type:', typeof electron)
if (typeof electron === 'object') {
  console.log('keys:', Object.keys(electron).slice(0, 10))
} else {
  console.log('value:', electron)
}
