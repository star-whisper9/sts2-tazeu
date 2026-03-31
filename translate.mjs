import fs from 'fs';
import path from 'path';

function listDocsFiles(dir, fileList = []) {
  const files = fs.readdirSync(dir);
  for (const file of files) {
    if (fs.statSync(path.join(dir, file)).isDirectory()) {
      listDocsFiles(path.join(dir, file), fileList);
    } else if (file.endsWith('.md')) { fileList.push(path.join(dir, file)); }
  }
  return fileList;
}
console.log(listDocsFiles('/Users/star/Documents/sts2_tazeu_docs/zh').join('\n'));
