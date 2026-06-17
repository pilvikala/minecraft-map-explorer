// This should be run as an electron main process
try {
  // Try accessing electron through a different path
  const { app } = require('electron')
  console.log('app:', app ? 'found' : 'undefined')
  if (app) {
    app.whenReady().then(() => { app.quit() })
  } else {
    process.exit(1)
  }
} catch(e) {
  console.error('Error:', e.message)
  process.exit(1)
}
